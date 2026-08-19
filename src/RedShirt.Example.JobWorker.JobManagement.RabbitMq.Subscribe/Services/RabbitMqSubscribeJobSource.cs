using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services;

internal class RabbitMqSubscribeJobSource(
    IRabbitMqChannelCacheSource channelSource,
    IRabbitMqRetryWrapperService retryWrapperService,
    IJobBacklogSizeService backlogSizeService,
    IJobIntakeService jobIntakeService,
    IOptions<RabbitMqSubscribeJobSource.ConfigurationModel> configuration,
    ILogger<RabbitMqSubscribeJobSource> logger)
    : IJobSource
{
    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var body = Encoding.UTF8.GetString(args.Body.ToArray());

        logger.LogTrace("Received message {MessageId} from RabbitMQ Queue: {QueueName}",
            args.BasicProperties.MessageId ?? "UNKNOWN", configuration.Value.QueueName);

        var job = new RabbitMqRawJobModel
        {
            MessageId = args.BasicProperties.MessageId ?? "UNKNOWN",
            IdempotencyId = args.BasicProperties.MessageId,
            DeliveryTag = args.DeliveryTag,
            CreatedAtUtc = DateTime.UtcNow,
            Body = body
        };

        await jobIntakeService.SubmitAsync(new JobSourceResponse
        {
            Items = [job]
        }, args.CancellationToken);
    }

    private async Task StartConsumerAsync(IChannel channel, CancellationToken cancellationToken)
    {
        logger.LogTrace("Subscribing to RabbitMQ Queue: {QueueName}", configuration.Value.QueueName);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await retryWrapperService.RunAsync(
            async ct =>
            {
                await channel.BasicQosAsync(
                    0, // no byte-size cap
                    Math.Max(ushort.MaxValue,
                        (ushort) Math.Min(ushort.MaxValue, backlogSizeService.BacklogSize)), // max unacked messages
                    false, // per consumer, not the whole channel
                    ct);
                await channel.BasicConsumeAsync(configuration.Value.QueueName, false, consumer, ct);
            },
            cancellationToken);
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not RabbitMqRawJobModel rabbitMqJobModel)
        {
            // Message did not originate from RabbitMQ, return
            return;
        }

        var channel = await channelSource.GetChannelAsync(cancellationToken);

        await retryWrapperService.RunAsync(async ct =>
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
        var channel = await channelSource.GetChannelAsync(cancellationToken);
        await StartConsumerAsync(channel, cancellationToken);

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
            recoverable.RecoveryAsync += async (_, args) =>
            {
                logger.LogInformation(
                    "RabbitMQ channel recovered; re-subscribing to queue {QueueName}",
                    configuration.Value.QueueName);
                var recoveredChannel = await channelSource.GetChannelAsync(args.CancellationToken);
                await StartConsumerAsync(recoveredChannel, args.CancellationToken);
            };
        }
    }

    public void StopSubscriber()
    {
        throw new NotImplementedException();
    }

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}