using Apache.NMS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

#pragma warning disable S107
internal class ActiveMqSubscribeJobSource(
    IActiveMqRetryWrapperService retryWrapperService,
    IActiveMqConsumerRetryWrapper consumerRetryWrapper,
    ICoreConfigurationService coreConfigurationService,
    IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
    IExecutionEndArbiter executionEndArbiter,
    ISleepService sleepService,
    IOptions<ActiveMqConfigurationModel> configuration,
    ILogger<ActiveMqSubscribeJobSource> logger)
    : IJobSource
#pragma warning restore S107
{
    private Task OnReceivedAsync(IMessage message, CancellationToken cancellationToken)
    {
        try
        {
            _ = cancellationToken;

            logger.LogTrace("Received message {MessageId} from ActiveMQ Queue: {QueueName}",
                message.NMSMessageId ?? "UNKNOWN", configuration.Value.QueueName);

            var job = new ActiveMqRawJobModel
            {
                Message = message,
                MessageId = message.NMSMessageId ?? "UNKNOWN",
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

    /// <summary>
    ///     Use a consumer to start an async listener.
    ///     Assumed to be invoked within a retry wrapper.
    /// </summary>
    /// <param name="consumer"></param>
    /// <param name="cancellationToken"></param>
    private Task StartConsumerAsync(IMessageConsumer consumer, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        logger.LogTrace("Subscribing to ActiveMQ Queue: {QueueName}", configuration.Value.QueueName);

        consumer.AsyncListener += OnReceivedAsync;

        logger.LogTrace("Subscribed to ActiveMQ Queue: {QueueName}", configuration.Value.QueueName);
        return Task.CompletedTask;
    }

    private void OnConnectionResumed()
    {
        logger.LogInformation("ActiveMQ connection resumed");
        // Unlike RabbitMQ, no need to resubscribe - handled by underlying client library
    }

    /// <summary>
    ///     Attempt to start the consumer, retrying according to transient / halt-on-failure configuration.
    ///     Keeping this in a separate method is a bit unnecessary, as opposed to RabbitMQ with its resubscribes.
    ///     However, keeping it in because I like the clean declaration in StartSubscriptionAsync.
    /// </summary>
    /// <param name="logVerb">
    ///     Verb used in error logs (e.g. "subscribing" or "re-subscribing").
    /// </param>
    /// <param name="cancellationToken"></param>
    private async Task SubscribeWithRetryLoopAsync(string logVerb, CancellationToken cancellationToken)
    {
        var firstIteration = true;
        while (true)
        {
            if (firstIteration)
            {
                firstIteration = false;
                await sleepService.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
            }

            try
            {
                await GetConsumerAndDoActionWithRetryAsync(StartConsumerAsync, cancellationToken);
            }
            catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)
            {
                // Pass
            }
#pragma warning disable S2139
            // Misguided sonar warning
            catch (Exception e)
#pragma warning restore S2139
            {
                // Some variety of non-transient failure
                logger.LogError(e, "Error {LogVerb} to ActiveMQ", logVerb);

                if (e is WorkerJobSourceException {CouldBeTransient: true} &&
                    !coreConfigurationService.IsTreatingTransientExceptionAsFailure())
                {
                    // Transient: Retry and try again
                    continue;
                }

                if (!coreConfigurationService.IsHaltOnFailure())
                {
                    // Not halting on failure, continue and try again
                    continue;
                }

                // HaltOnFailure is true.
                // Pass the exception up to one of our threads as opposed to an ActiveMQ-managed one
                executionEndArbiter.Stop(e);
                // Fall through to break out of loop
            }

            break;
        }
    }

    private Task GetConsumerAndDoActionWithRetryAsync(Func<IMessageConsumer, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        return consumerRetryWrapper.GetChannelAndDoActionWithRetryAsync(callback, OnNewConnection,
            cancellationToken: cancellationToken);
    }

    private void OnNewConnection(IConnection connection)
    {
        connection.ConnectionResumedListener -= OnConnectionResumed;
        connection.ConnectionResumedListener += OnConnectionResumed;
    }

    private async Task WaitThenStopSubscriberAsync(CancellationToken cancellationToken = default)
    {
        await executionEndArbiter.WaitForFinishedAsync(cancellationToken);

        try
        {
            await GetConsumerAndDoActionWithRetryAsync(
                (consumer, _) =>
                {
                    consumer.AsyncListener -= OnReceivedAsync;

                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not unsubscribe: {Message}", exception.Message);
            // Not terribly concerned about any other exceptions because it's assumed in the shutdown period anyway.
            // But just in case...
        }
    }

    /// <summary>
    ///     Acknowledge a message.
    ///     Same client-ack behaviour as <see cref="ActiveMqJobSource" />.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="result"></param>
    /// <param name="cancellationToken"></param>
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not ActiveMqRawJobModel jobModel)
        {
            // Message did not originate from ActiveMQ, return
            return;
        }

        // Intentionally not using result for ack/nack branching — NMS ClientAcknowledge has no
        // direct dead-letter / requeue call here analogous to RabbitMQ BasicNack.
        _ = result;

        await retryWrapperService.RunAsync(
            _ => jobModel.Message.AcknowledgeAsync(),
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
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IMessage / IConnection objects.
         */
        return Task.CompletedTask;
    }

    public async Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        // Kick off the task that shall watch for unsubscribes
        _ = Task.Run(() => WaitThenStopSubscriberAsync(cancellationToken), cancellationToken);

        await SubscribeWithRetryLoopAsync("subscribing", cancellationToken);
    }
}