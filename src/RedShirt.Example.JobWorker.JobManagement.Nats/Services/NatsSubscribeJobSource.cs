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

internal class NatsSubscribeJobSource : IJobSource, IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly INatsConnectionRetryWrapper _connectionRetryWrapper;
    private readonly ICoreConfigurationService _coreConfigurationService;
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly Lock _generalLock = new();
    private readonly IJobSubscriberIntakeQueue _jobSubscriberIntakeQueue;
    private readonly ILogger<NatsSubscribeJobSource> _logger;
    private readonly IOptions<NatsStreamConfigurationModel> _options;
    private readonly INatsRetryWrapperService _retryWrapperService;
    private readonly ISleepService _sleepService;
    private bool _disposed;
    private bool _subscribeLoopRunning;

    private void OnExecutionStop(Exception? exception)
    {
        _ = exception;

        lock (_generalLock)
        {
            if (_disposed)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
        }
    }

    private void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_generalLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private async Task DoOperationWithLinkedToken(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Construct a linked CTS to tie it to _cancellationTokenSource.
         *
         * Note: During development, I experimented with a GetLinkedToken method,
         * but that ended up being invalid. The reason it was invalid is that
         *  disposing a linked source unregisters it from _cancellationTokenSource.
         *  The CancellationToken that was being returned was no longer hooked to that source,
         *  making the end result just the baseline cancellation token with extra steps.
         */

        CancellationTokenSource? linkedCts = null;
        try
        {
            CancellationToken linkedToken;
            lock (_generalLock)
            {
                if (_disposed)
                {
                    throw new OperationCanceledException();
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token);
                linkedToken = linkedCts.Token;
            }

            await operation(linkedToken);
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    private async Task StartConsumerAsync(INatsJSConsumer consumer, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Consuming NATS Stream: {StreamName}", _options.Value.StreamName);

        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = Math.Max(1, _coreConfigurationService.FetchCount)
        };

        await foreach (var msg in consumer.ConsumeAsync<NatsMemoryOwner<byte>>(null, consumeOpts,
                           cancellationToken))
        {
            _logger.LogTrace("Received message from NATS Stream: {StreamName}",
                _options.Value.StreamName);

            var job = new NatsRawJobModel
            {
                Message = msg,
                MessageId = msg.Metadata?.Sequence.Stream.ToString() ?? "UNKNOWN",
                CreatedAtUtc = DateTime.UtcNow
            };

            _jobSubscriberIntakeQueue.Load(new JobSourceResponse
            {
                Items = [job]
            });
        }
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
                    await _sleepService.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                }

                firstIteration = false;

                try
                {
                    await _connectionRetryWrapper.GetConsumerAndDoActionWithRetryAsync(StartConsumerAsync,
                        forceNewConnectionImmediately,
                        cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException e)
                {
                    // Pass to break later
                }
#pragma warning disable S2139
                catch (Exception e)
#pragma warning restore S2139
                {
                    _logger.LogError(e, "Error {LogVerb} to NATS", logVerb);

                    if (e is WorkerJobSourceException {CouldBeTransient: true} &&
                        !_coreConfigurationService.IsTreatingTransientExceptionAsFailure)
                    {
                        continue;
                    }

                    if (!_coreConfigurationService.IsHaltOnFailure)
                    {
                        continue;
                    }

                    _executionEndArbiter.Stop(e);
                }

                break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _subscribeLoopRunning, false);
        }
    }

#pragma warning disable S107
    public NatsSubscribeJobSource(
        INatsConnectionRetryWrapper connectionRetryWrapper,
        INatsRetryWrapperService retryWrapperService,
        ICoreConfigurationService coreConfigurationService,
        IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
        IExecutionEndArbiter executionEndArbiter,
        ISleepService sleepService,
        IOptions<NatsStreamConfigurationModel> options,
        ILogger<NatsSubscribeJobSource> logger)
#pragma warning restore S107
    {
        _connectionRetryWrapper = connectionRetryWrapper;
        _retryWrapperService = retryWrapperService;
        _coreConfigurationService = coreConfigurationService;
        _jobSubscriberIntakeQueue = jobSubscriberIntakeQueue;
        _executionEndArbiter = executionEndArbiter;
        _sleepService = sleepService;
        _options = options;
        _logger = logger;

        executionEndArbiter.AddOnStopCallback(OnExecutionStop);
    }

    public void Dispose()
    {
        Dispose(true);
        // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
        GC.SuppressFinalize(this);
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not NatsRawJobModel jobModel)
        {
            return;
        }

        _ = result;

        await _retryWrapperService.RunAsync(
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

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        // Kick off SubscribeWithRetryLoopAsync in a thread because the implementation is blocking.
        _ = Task.Run(() => DoOperationWithLinkedToken(
                ct => SubscribeWithRetryLoopAsync("subscribing", false, ct),
                cancellationToken),
            cancellationToken);
        return Task.CompletedTask;
    }
}