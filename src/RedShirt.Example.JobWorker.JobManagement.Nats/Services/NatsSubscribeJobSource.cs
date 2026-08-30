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
    IOptions<NatsStreamConfigurationModel> options,
    ILogger<NatsSubscribeJobSource> logger)
    : IJobSource
#pragma warning restore S107
{
    private CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _subscribeLoopRunning;

    private void OnExecutionStop(Exception? exception)
    {
        _cancellationTokenSource.Cancel();
    }

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

        await foreach (var msg in consumer.ConsumeAsync<NatsMemoryOwner<byte>>(null, consumeOpts,
                           cancellationToken))
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
                    await connectionRetryWrapper.GetConsumerAndDoActionWithRetryAsync(StartConsumerAsync,
                        forceNewConnectionImmediately,
                        cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)
                {
                    // Pass to break later
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