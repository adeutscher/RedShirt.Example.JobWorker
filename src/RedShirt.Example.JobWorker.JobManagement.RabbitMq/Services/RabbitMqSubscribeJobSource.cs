using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

#pragma warning disable S107
internal class RabbitMqSubscribeJobSource(
    IRabbitMqChannelRetryWrapper channelRetryWrapper,
    ICoreConfigurationService coreConfigurationService,
    IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
    IExecutionEndArbiter executionEndArbiter,
    ISleepService sleepService,
    IRabbitMqSubscribeExceptionArbiter subscribeExceptionArbiter,
    IOptions<RabbitMqQueueConfigurationModel> rabbitMqConfiguration,
    ILogger<RabbitMqSubscribeJobSource> logger)
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

    private string? _subscriberTag;

    private Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var body = Encoding.UTF8.GetString(args.Body.ToArray());

            logger.LogTrace("Received message {MessageId} from RabbitMQ Queue: {QueueName}",
                args.BasicProperties.MessageId ?? "UNKNOWN", rabbitMqConfiguration.Value.QueueName);

            var job = new RabbitMqRawJobModel
            {
                MessageId = args.BasicProperties.MessageId ?? "UNKNOWN",
                IdempotencyId = args.BasicProperties.MessageId,
                DeliveryTag = args.DeliveryTag,
                CreatedAtUtc = DateTime.UtcNow,
                Body = body
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
    ///     Use a channel to start a consumer.
    ///     Assumed to be invoked within a retry wrapper.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="cancellationToken"></param>
    private async Task StartConsumerAsync(IChannel channel, CancellationToken cancellationToken)
    {
        logger.LogTrace("Subscribing to RabbitMQ Queue: {QueueName}", rabbitMqConfiguration.Value.QueueName);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        // Reminder: Not doing any retry wrapping here because it is assumed to already be done.

        await channel.BasicQosAsync(
            0, // no byte-size cap
            // Set backlog size according to Core's configured buffer (within the limits of usage of ushort in RabbitMQ)
            Math.Max(ushort.MaxValue,
                (ushort) Math.Min(ushort.MaxValue, coreConfigurationService.FetchCount)), // max unacked messages
            false, // per consumer, not the whole channel
            cancellationToken);
        _subscriberTag =
            await channel.BasicConsumeAsync(rabbitMqConfiguration.Value.QueueName, false, consumer, cancellationToken);

        logger.LogTrace("Subscribed to RabbitMQ Queue: {QueueName}", rabbitMqConfiguration.Value.QueueName);
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
                    await GetChannelAndDoActionWithRetryAsync(StartConsumerAsync, cancellationToken);
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
                    logger.LogError(e, "Error {LogVerb} to RabbitMQ", logVerb);

                    if (e is WorkerJobSourceException {CouldBeTransient: true} &&
                        !coreConfigurationService.IsTreatingTransientExceptionAsFailure)
                    {
                        // Transient: Retry and try again
                        continue;
                    }

                    if (!coreConfigurationService.IsHaltOnFailure)
                    {
                        // Not halting on failure, continue and try again
                        continue;
                    }

                    // HaltOnFailure is true.
                    // Pass the exception up to one of our threads as opposed to a RabbitMQ-managed one
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

    private Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        return channelRetryWrapper.GetChannelAndDoActionWithRetryAsync(callback,
            OnNewConnection,
            cancellationToken);
    }

    /// <summary>
    ///     Handle RabbitMQ connection shutdown.
    ///     Intended to handle network connection problems and initiate a reconnect when AutomaticRecovery is disabled.
    /// </summary>
    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs args)
    {
        /*
         * ConnectionShutdownAsync is the reconnect signal when AutomaticRecovery is disabled for subscriptions.
         * Application-initiated closes (unsubscribe / dispose) should not trigger reconnect.
         */

        if (args.Initiator == ShutdownInitiator.Application)
        {
            return Task.CompletedTask;
        }

        var exception = args.Exception ?? new AlreadyClosedException(args);

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

            // Avoid spawning another reconnect task while a subscribe loop is already in flight.
            if (Volatile.Read(ref _subscribeLoopRunning))
            {
                return Task.CompletedTask;
            }

            logger.LogWarning(exception, "RabbitMQ connection shutdown, reconnecting");

            channelRetryWrapper.ResetChannel();

            _ = Task.Run(() => SubscribeWithRetryLoopAsync("re-subscribing", _cancellationToken), _cancellationToken);
            return Task.CompletedTask;
        }

        if (subscribeExceptionArbiter.IsAccountedForAndLikelyTransientError(exception))
        {
            // Is an expected transient error, not worth warning about
            return Task.CompletedTask;
        }

        logger.LogWarning(exception,
            "Unaccounted-for exception in {Name}. Classify via {IRabbitMqSubscribeExceptionArbiter} methods",
            nameof(RabbitMqSubscribeJobSource),
            nameof(IRabbitMqSubscribeExceptionArbiter));
        return Task.CompletedTask;
    }

    private void OnNewConnection(IConnection connection)
    {
        // Subscription mode disables AutomaticRecovery; ConnectionShutdownAsync drives our reconnect loop
        // (including credential rotation after a drop). Do not hook RecoverySucceededAsync.
        connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
        connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
    }

    private async Task WaitThenStopSubscriberAsync(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;

        await executionEndArbiter.WaitForFinishedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(_subscriberTag))
        {
            try
            {
                await GetChannelAndDoActionWithRetryAsync(
                    (channel, ct) => channel.BasicCancelAsync(_subscriberTag, cancellationToken: ct),
                    cancellationToken);
            }
            catch (WorkerJobSourceException e) when (e.InnerException is AlreadyClosedException)
            {
                // Pass, connection is already dead and we're shutting down the consumer. Not even noteworthy.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not unsubscribe: {Message}", exception.Message);
                // Not terribly concerned about any other exceptions because it's assumed in the shutdown period anyway.
                // But just in case...
            }
        }
    }

    /// <summary>
    ///     Acknowledge a message.
    ///     Note: Almost but not quite the same as the original RabbitMqJobSource implementation of AcknowledgeAsync,
    ///     preventing consolidation.
    ///     Under the hood, using a different reconnect handler. Not worth it to branch that out to grasp at line reduction.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="result"></param>
    /// <param name="cancellationToken"></param>
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not RabbitMqRawJobModel rabbitMqJobModel)
        {
            // Message did not originate from RabbitMQ, return
            return;
        }

        await GetChannelAndDoActionWithRetryAsync(async (channel, ct) =>
        {
            if (result.IsSuccessful())
            {
                await channel.BasicAckAsync(rabbitMqJobModel.DeliveryTag, false, ct);
            }
            else
            {
                // If recoverable, then NAck with requeue so the message can be delivered again.
                // Empty / Parsing / InvalidData: NAck without requeue (dead-letter if the queue is configured for it).
                await channel.BasicNackAsync(rabbitMqJobModel.DeliveryTag, false, result.IsRecoverableFailure(),
                    ct);
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
         * Not necessary. Heartbeats are managed by the persistence of the IConnection object.
         * See: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
         * Since it is not necessary to do any thinking, then it is also not necessary to check that the provided
         * IRawJobDataModel is even a RabbitMqRawJobModel.
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