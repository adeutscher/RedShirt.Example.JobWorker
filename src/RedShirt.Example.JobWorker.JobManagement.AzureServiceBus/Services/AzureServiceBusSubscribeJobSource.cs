using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

#pragma warning disable S107
internal class AzureServiceBusSubscribeJobSource(
    IAzureServiceBusClientRetryWrapper clientRetryWrapper,
    IAzureServiceBusRetryWrapperService retryWrapperService,
    ICoreConfigurationService coreConfigurationService,
    IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
    IExecutionEndArbiter executionEndArbiter,
    ISleepService sleepService,
    IAzureServiceBusDetailedExceptionArbiter detailedExceptionArbiter,
    IOptions<AzureServiceBusConfigurationModel> options,
    ILogger<AzureServiceBusSubscribeJobSource> logger)
    : IJobSource
#pragma warning restore S107
{
    private CancellationToken _cancellationToken;
    private bool _subscribeLoopRunning;
    private IServiceBusProcessorWrapper? _activeProcessor;

    private Task OnReceivedAsync(ProcessMessageEventArgs args)
    {
        try
        {
            logger.LogTrace("Received message {MessageId} from Azure Service Bus",
                args.Message.MessageId ?? "UNKNOWN");

            var container = new ServiceBusClientWrapper.ServiceBusMessageContainer
            {
                Message = args.Message
            };

            var job = new AzureRawJobModel
            {
                Message = container,
                Settler = new ProcessMessageSettler(args),
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

    private async Task StartProcessorAsync(IServiceBusProcessorWrapper processor, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        logger.LogTrace("Subscribing to Azure Service Bus queue");

        processor.ProcessMessageAsync += OnReceivedAsync;
        _activeProcessor = processor;
        await processor.StartProcessingAsync(cancellationToken);

        logger.LogTrace("Subscribed to Azure Service Bus queue");
    }

    private async Task SubscribeWithRetryLoopAsync(string logVerb, bool forceNewClientImmediately,
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
                    await GetProcessorAndDoActionWithRetryAsync(StartProcessorAsync, forceNewClientImmediately,
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
                    logger.LogError(e, "Error {LogVerb} to Azure Service Bus", logVerb);

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

    private Task GetProcessorAndDoActionWithRetryAsync(Func<IServiceBusProcessorWrapper, CancellationToken, Task> callback,
        bool forceNewClientImmediately,
        CancellationToken cancellationToken)
    {
        return clientRetryWrapper.GetProcessorAndDoActionWithRetryAsync(callback,
            forceNewClientImmediately,
            OnNewProcessor,
            cancellationToken);
    }

    private Task OnProcessorErrorAsync(ProcessErrorEventArgs args)
    {
        var exception = args.Exception;

        if (detailedExceptionArbiter.IsReasonToReconnect(exception)
            || detailedExceptionArbiter.IsReasonToStopIfHaltOnFailure(exception))
        {
            if (Volatile.Read(ref _subscribeLoopRunning))
            {
                return Task.CompletedTask;
            }

            logger.LogWarning(exception, "Azure Service Bus processor error, reconnecting");

            clientRetryWrapper.ResetProcessor();

            _ = Task.Run(() => SubscribeWithRetryLoopAsync("re-subscribing", true, _cancellationToken),
                _cancellationToken);
            return Task.CompletedTask;
        }

        if (detailedExceptionArbiter.IsAccountedForAndLikelyTransientError(exception))
        {
            return Task.CompletedTask;
        }

        logger.LogWarning(exception,
            "Unaccounted-for exception in {Name}. Classify via {IAzureServiceBusDetailedExceptionArbiter} methods",
            nameof(AzureServiceBusSubscribeJobSource),
            nameof(IAzureServiceBusDetailedExceptionArbiter));
        return Task.CompletedTask;
    }

    private void OnNewProcessor(IServiceBusProcessorWrapper processor)
    {
        processor.ProcessErrorAsync += OnProcessorErrorAsync;
    }

    private async Task WaitThenStopSubscriberAsync(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;

        await executionEndArbiter.WaitForFinishedAsync(cancellationToken);

        if (_activeProcessor is not null)
        {
            try
            {
                await GetProcessorAndDoActionWithRetryAsync(async (processor, ct) =>
                {
                    processor.ProcessMessageAsync -= OnReceivedAsync;
                    await processor.StopProcessingAsync(ct);
                }, false, cancellationToken);
            }
            catch (WorkerJobSourceException e) when (e.InnerException is ObjectDisposedException)
            {
                // Pass, client is already dead and we're shutting down.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not unsubscribe: {Message}", exception.Message);
            }
        }
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel || messageAsAzureJobModel.Settler is null)
        {
            return;
        }

        var settler = messageAsAzureJobModel.Settler;

        await retryWrapperService.RunAsync(async ct =>
        {
            if (result.IsSuccessful())
            {
                await settler.CompleteMessageAsync(ct);
            }
            else if (result.IsRecoverableFailure())
            {
                if (options.Value.AbandonRecoveredFailuresOnAcknowledge)
                {
                    await settler.AbandonMessageAsync(ct);
                }
            }
            else
            {
                await settler.DeadLetterMessageAsync(result.ToString(), cancellationToken: ct);
            }
        }, cancellationToken);
    }

    public Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public bool IsSubscriptionSource => true;

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Lock renewal is managed by the Service Bus processor while the handler runs.
         */
        return Task.CompletedTask;
    }

    public async Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        _ = Task.Run(() => WaitThenStopSubscriberAsync(cancellationToken), cancellationToken);

        await SubscribeWithRetryLoopAsync("subscribing", false, cancellationToken);
    }
}
