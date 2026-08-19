using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.Core.Utility;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services;

#pragma warning disable S107
internal class RabbitMqSubscribeJobSource(
    IRabbitMqChannelCacheSource channelSource,
    IRabbitMqRetryWrapperService retryWrapperService,
    IJobBacklogSizeService backlogSizeService,
    IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
    IExecutionEndArbiter executionEndArbiter,
    IOptions<CoreConfigurationModel> coreOptions,
    IOptions<RabbitMqSubscribeJobSource.ConfigurationModel> rabbitMqConfiguration,
    ILogger<RabbitMqSubscribeJobSource> logger)
#pragma warning restore S107
    : IJobSource
{
    private readonly AsyncManualResetEvent _subscriberCancelEvent = new();

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
            Math.Max(ushort.MaxValue,
                (ushort) Math.Min(ushort.MaxValue, backlogSizeService.BacklogSize)), // max unacked messages
            false, // per consumer, not the whole channel
            cancellationToken);
        _subscriberTag =
            await channel.BasicConsumeAsync(rabbitMqConfiguration.Value.QueueName, false, consumer, cancellationToken);
    }

    private async Task OnRecoveryAsync(object _, AsyncEventArgs args)
    {
        logger.LogInformation(
            "RabbitMQ channel recovered; re-subscribing to queue {QueueName}",
            rabbitMqConfiguration.Value.QueueName);

        while (true)
        {
            try
            {
                await GetChannelAndDoActionWithRetryAsync(StartConsumerAsync, args.CancellationToken);
            }
            catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)
            {
                // Pass
            }
            catch (WorkerJobSourceException e) when (e.CouldBeTransient)
            {
                logger.LogWarning(e, "Error re-subscribing to RabbitMQ");
                // Continue to try again
                continue;
            }
            catch (Exception e)
            {
                // Some variety of non-transient failure
                logger.LogError(e, "Error re-subscribing to RabbitMQ");
                if (!coreOptions.Value.HaltOnFailure)
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

    private void StopSubscriber()
    {
        // Need to middleman through a manual reset event in order to make async calls.
        _subscriberCancelEvent.Set();
    }

    private async Task WaitThenStopSubscriberAsync(CancellationToken cancellationToken = default)
    {
        await _subscriberCancelEvent.WaitAsync(cancellationToken);

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
                // Not terribly concerned about this exception because it's in the shutdown period anyway, but just in case...
            }
        }
    }

    private Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        return retryWrapperService.RunAsync(async (state, ct) =>
        {
            state.AttemptNumber++;
            // Force getting a new channel if this is beyond the first attempt.
            var channel = await channelSource.GetChannelAsync(state.AttemptNumber > 1, ct);
            await callback(channel, cancellationToken);
        }, new ChannelState
        {
            AttemptNumber = 0
        }, cancellationToken);
    }

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
        // Kick off the waiting task
        executionEndArbiter.AddOnStopCallback(_ => StopSubscriber());
        _ = Task.Run(() => WaitThenStopSubscriberAsync(cancellationToken), cancellationToken);

        IChannel? channel = null;
        while (true)
        {
            try
            {
                await GetChannelAndDoActionWithRetryAsync(StartConsumerAsync, cancellationToken);
            }
            catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)
            {
                // Pass
            }
            catch (WorkerJobSourceException e) when (e.CouldBeTransient)
            {
                logger.LogWarning(e, "Error subscribing to RabbitMQ");
                // Continue to try again
                continue;
            }
#pragma warning disable S2139
            // Misguided sonar warning
            catch (Exception e)
#pragma warning restore S2139
            {
                // Some variety of non-transient failure
                logger.LogError(e, "Error subscribing to RabbitMQ");
                if (!coreOptions.Value.HaltOnFailure)
                {
                    // Not halting on failure, continue and try again
                    continue;
                }

                // Unlike OnRecoveryAsync, we aren't in some thread kicked off by RabbitMQ.
                // Just throw it upwards.
                throw;
            }

            break;
        }

        /*
         * The hardcoded AutomaticRecoveryEnabled property on the RabbitMQ connection parameters restores the connection
         * and channel after a network failure. Topology recovery would also re-register consumers, so
         * TopologyRecoveryEnabled is false: this worker does not declare topology (the queue is owned elsewhere),
         * and StartConsumerAsync is the subscribe path (retry wrapper, logging, a new AsyncEventingBasicConsumer).
         *
         * If topology recovery had stayed on, then the client would restore the old consumer and this handler
         * would BasicConsume again, leaving two competing consumers on the same queue.
         * Re-subscribe here when the channel recovers.
         */
        if (channel is IRecoverable recoverable)
        {
            recoverable.RecoveryAsync += OnRecoveryAsync;
        }
    }

    private sealed class ChannelState
    {
        public required int AttemptNumber { get; set; }
    }

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}