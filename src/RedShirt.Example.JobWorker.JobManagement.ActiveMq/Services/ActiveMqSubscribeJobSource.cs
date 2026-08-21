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
    IActiveMqSubscribeExceptionArbiter subscribeExceptionArbiter,
    IOptions<ActiveMqConfigurationModel> configuration,
    ILogger<ActiveMqSubscribeJobSource> logger)
    : IJobSource
#pragma warning restore S107
{
    /// <summary>
    ///     Cancellation token provided when subscription started.
    /// </summary>
    private CancellationToken _cancellationToken;

    /// <summary>
    ///     Whether <see cref="SubscribeWithRetryLoopAsync" /> is currently running.
    /// </summary>
    private bool _subscribeLoopRunning;

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
        logger.LogInformation("ActiveMQ connection established");
        // Unlike RabbitMQ, no need to resubscribe - handled by underlying client library
    }

    /// <summary>
    ///     Attempt to start the consumer, retrying according to transient / halt-on-failure configuration.
    ///     Only one invocation may run at a time; concurrent callers return immediately.
    /// </summary>
    /// <param name="logVerb">
    ///     Verb used in error logs (e.g. "subscribing" or "re-subscribing").
    /// </param>
    /// <param name="cancellationToken"></param>
    private async Task SubscribeWithRetryLoopAsync(string logVerb, CancellationToken cancellationToken)
    {
        // CompareExchange returns the prior value; true means another caller already holds the lock.
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
        finally
        {
            Interlocked.Exchange(ref _subscribeLoopRunning, false);
        }
    }

    private Task GetConsumerAndDoActionWithRetryAsync(Func<IMessageConsumer, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        return consumerRetryWrapper.GetChannelAndDoActionWithRetryAsync(callback, OnNewConnection,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Handle ActiveMQ exceptions.
    ///     Intended to handle network connection problems and initiate a reconnect.
    /// </summary>
    /// <param name="exception"></param>
    private void OnException(Exception exception)
    {
        /*
         * ExceptionListener is the reconnect signal when not using NMS failover.
         * However, ExceptionListener casts a wider net that we need to filter out.
         *
         * During development this was originally done using the ActiveMQ library's built-in fail-over settings,
         * which were enforced on the broker URI in the connection factory. However, this did not cover the niche
         * case of what might happen if the connection was interrupted AND the credentials changed.
         * Also explained in the connection factory.
         */

        if (subscribeExceptionArbiter.IsReasonToReconnect(exception)
            || subscribeExceptionArbiter.IsReasonToStopIfHaltOnFailure(exception))
        {
            /*
             * Is an explicit reason to reconnect or another serious error. Funnel both through reconnection.
             *
             * If it is a known reason to reconnect, then reconnect is exactly what we'll do.
             * If it is another error, then the reconnect serves a few different purposes:
             *  * The reconnect is aware of the established retry loop and the main exception arbiter, allowing both to weigh in.
             *  * If HaltOnFailure is false, then it allows the subscriber a chance to recover
             *  * If HaltOnFailure is true, then the established retry loop still stops the application
             */

            // We want to kick off a worker thread to reconnect and resubscribe.
            // Not doing it here because we are not in an async method.

            // Avoid spawning another reconnect task while a subscribe loop is already in flight.
            if (Volatile.Read(ref _subscribeLoopRunning))
            {
                return;
            }

            logger.LogWarning(exception, "ActiveMQ ExceptionListener problem, reconnecting");

            consumerRetryWrapper.ResetConsumer();

            _ = Task.Run(() => SubscribeWithRetryLoopAsync("re-subscribing", _cancellationToken), _cancellationToken);
            return;
        }

        if (subscribeExceptionArbiter.IsAccountedForAndLikelyTransientError(exception))
        {
            // Is an expected transient error, not worth warning about
            return;
        }

        logger.LogWarning(exception,
            "Unaccounted-for exception in {Name}. Classify via {IActiveMqSubscribeExceptionArbiter} methods",
            nameof(ActiveMqSubscribeJobSource),
            nameof(IActiveMqSubscribeExceptionArbiter));
    }

    private void OnNewConnection(IConnection connection)
    {
        // Deliberately using ExceptionListener instead of ConnectionInterruptedListener.
        // ConnectionInterruptedListener is not used when not using fail-over
        //  (as is enforced in the factory for subscribe mode, see there for justification)
        connection.ExceptionListener -= OnException;
        connection.ExceptionListener += OnException;
        connection.ConnectionResumedListener -= OnConnectionResumed;
        connection.ConnectionResumedListener += OnConnectionResumed;
    }

    private async Task WaitThenStopSubscriberAsync(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;

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

        // Intentionally not using result
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue
        _ = result;

        // Acknowledge whether successful, recoverable, or unrecoverable
        // (ActiveMQ client API has no direct dead-letter call here).
        // Noting that it is very intentional that we use the base IActiveMqRetryWrapperService here.
        // An exception here is not going to be anything that we could solve with a reconnect.
        // In fact, it would only cause more problems.
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
        // Kick off the task that shall watch for unsubscribes when the application stops
        _ = Task.Run(() => WaitThenStopSubscriberAsync(cancellationToken), cancellationToken);

        await SubscribeWithRetryLoopAsync("subscribing", cancellationToken);
    }
}