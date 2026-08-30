using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

#pragma warning disable S107
internal class NatsSubscribeJobSource(
    INatsConnectionRetryWrapper connectionRetryWrapper,
    INatsRetryWrapperService retryWrapperService,
    ICoreConfigurationService coreConfigurationService,
    IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
    IExecutionEndArbiter executionEndArbiter,
    ISleepService sleepService,
    INatsSubscribeExceptionArbiter subscribeExceptionArbiter,
    IOptions<NatsStreamConfigurationModel> options,
    ILogger<NatsSubscribeJobSource> logger)
    : IJobSource
#pragma warning restore S107
{
    private CancellationToken _cancellationToken;
    private bool _subscribeLoopRunning;

    private Task OnReceivedAsync(INatsJSMsg<NatsMemoryOwner<byte>> msg)
    {
        try
        {
            logger.LogTrace("Received message from NATS Stream: {StreamName}",
                options.Value.StreamName);

            var job = new NatsRawJobModel
            {
                Message = msg,
                MessageId = msg.Metadata?.Sequence.Stream.ToString() ?? "UNKNOWN",
                CreatedAtUtc = DateTime.UtcNow
            };

            jobSubscriberIntakeQueue.Load(new JobSourceResponse
            {
                Items = [job]
            });
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private async Task StartConsumerAsync(INatsJSConsumer consumer, CancellationToken cancellationToken)
    {
        logger.LogTrace("Subscribing to NATS Stream: {StreamName}", options.Value.StreamName);

        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = Math.Max(1, coreConfigurationService.FetchCount)
        };

        await foreach (var msg in consumer.ConsumeAsync<NatsMemoryOwner<byte>>(serializer: null, opts: consumeOpts,
                           cancellationToken: cancellationToken))
        {
            await OnReceivedAsync(msg);
        }

        logger.LogTrace("Subscribed to NATS Stream: {StreamName}", options.Value.StreamName);
    }

    private async Task SubscribeWithRetryLoopAsync(string logVerb, bool forceNewConnectionImmediately,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _subscribeLoopRunning, true, false))
        {
            return;
        }

        try
        {
            var firstIteration = true;
            while (true)
            {
                if (!firstIteration)
                {
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                }

                firstIteration = false;

                try
                {
                    await GetConsumerAndDoActionWithRetryAsync(StartConsumerAsync, forceNewConnectionImmediately,
                        cancellationToken);
                }
                catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)
                {
                    // Pass
                }
#pragma warning disable S2139
                catch (Exception e)
#pragma warning restore S2139
                {
                    logger.LogError(e, "Error {LogVerb} to NATS", logVerb);

                    if (e is WorkerJobSourceException {CouldBeTransient: true} &&
                        !coreConfigurationService.IsTreatingTransientExceptionAsFailure)
                    {
                        continue;
                    }

                    if (!coreConfigurationService.IsHaltOnFailure)
                    {
                        continue;
                    }

                    executionEndArbiter.Stop(e);
                }

                break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _subscribeLoopRunning, false);
        }
    }

    private Task GetConsumerAndDoActionWithRetryAsync(
        Func<INatsJSConsumer, CancellationToken, Task> callback,
        bool forceNewConnectionImmediately,
        CancellationToken cancellationToken)
    {
        return connectionRetryWrapper.GetConsumerAndDoActionWithRetryAsync(callback,
            forceNewConnectionImmediately,
            OnNewConnection,
            cancellationToken);
    }

    private ValueTask OnConnectionDisconnectedAsync(object? sender, NatsEventArgs args)
    {
        var exception = new NatsConnectionFailedException(!string.IsNullOrWhiteSpace(args.Message) ? args.Message : "NATS connection disconnected");

        if (subscribeExceptionArbiter.IsReasonToReconnect(exception)
            || subscribeExceptionArbiter.IsReasonToStopIfHaltOnFailure(exception))
        {
            if (Volatile.Read(ref _subscribeLoopRunning))
            {
                return ValueTask.CompletedTask;
            }

            logger.LogWarning(exception, "NATS connection disconnected, reconnecting");

            connectionRetryWrapper.ResetConnection();

            _ = Task.Run(() => SubscribeWithRetryLoopAsync("re-subscribing", true, _cancellationToken),
                _cancellationToken);
            return ValueTask.CompletedTask;
        }

        if (subscribeExceptionArbiter.IsAccountedForAndLikelyTransientError(exception))
        {
            return ValueTask.CompletedTask;
        }

        logger.LogWarning(exception,
            "Unaccounted-for exception in {Name}. Classify via {INatsSubscribeExceptionArbiter} methods",
            nameof(NatsSubscribeJobSource),
            nameof(INatsSubscribeExceptionArbiter));
        return ValueTask.CompletedTask;
    }

    private ValueTask OnReconnectFailedAsync(object? sender, NatsEventArgs args)
    {
        return OnConnectionDisconnectedAsync(sender, args);
    }

    private void OnNewConnection(INatsConnection connection)
    {
        connection.ConnectionDisconnected -= OnConnectionDisconnectedAsync;
        connection.ConnectionDisconnected += OnConnectionDisconnectedAsync;
        connection.ReconnectFailed -= OnReconnectFailedAsync;
        connection.ReconnectFailed += OnReconnectFailedAsync;
    }

    private async Task WaitThenStopSubscriberAsync(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;

        await executionEndArbiter.WaitForFinishedAsync(cancellationToken);
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not NatsRawJobModel jobModel)
        {
            return;
        }

        _ = result;

        await retryWrapperService.RunAsync(
            async ct => await jobModel.Message.AckAsync(cancellationToken: ct),
            cancellationToken);
    }

    public Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public bool IsSubscriptionSource => true;

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        _ = Task.Run(() => WaitThenStopSubscriberAsync(cancellationToken), cancellationToken);

        await SubscribeWithRetryLoopAsync("subscribing", false, cancellationToken);
    }
}
