using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

internal class RabbitMqJobSource(
    IRabbitMqChannelCacheSource channelSource,
    IOptions<RabbitMqJobSource.ConfigurationModel> configuration,
    ILogger<RabbitMqJobSource> logger,
    IRabbitMqRetryWrapperService retryWrapperService)
    : IJobSource
{
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not RabbitMqRawJobModel rabbitMqJobModel)
        {
            // Message did not originate from RabbitMQ, return
            return;
        }

        var channel = await channelSource.GetChannelAsync(cancellationToken);
        try
        {
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
        catch (ObjectDisposedException e)
        {
            // An ObjectDisposedException suggests that the underlying connection behind this message was lost,
            //  meaning that this JobWorker process lost custody of the message under this delivery tag.  
            throw new WorkerJobSourceException(e)
            {
                IsHandled = true,
                CouldBeTransient = false,
                CouldBeExternallySolvable = false
            };
        }
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from RabbitMQ Queue: {QueueName}",
            batchSize, configuration.Value.QueueName);

        var getJobsResponseItems = new List<IRawJobModel>();

        var channel = await channelSource.GetChannelAsync(cancellationToken);

        while (getJobsResponseItems.Count < batchSize)
        {
            var result =
                await retryWrapperService.RunAsync(ct =>
                    channel.BasicGetAsync(configuration.Value.QueueName, false, ct), cancellationToken);

            if (result is null)
                // Nothing more to grab at the moment.
            {
                break;
            }

            var body = Encoding.UTF8.GetString(result.Body.ToArray());

            // Got a message, add it to return set.
            getJobsResponseItems.Add(new RabbitMqRawJobModel
            {
                MessageId = result.BasicProperties.MessageId ?? "UNKNOWN",
                IdempotencyId = result.BasicProperties.MessageId,
                DeliveryTag = result.DeliveryTag,
                CreatedAtUtc = DateTime.UtcNow,
                Body = body
            });
        }

        return new JobSourceResponse
        {
            Items = getJobsResponseItems
        };
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

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

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}