using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

internal class RabbitMqJobSource(
    IRabbitMqConnectionCacheSource connectionCacheSource,
    IRabbitMqRetryWrapperService retryWrapperService,
    IOptions<RabbitMqJobSource.ConfigurationModel> configuration,
    ILogger<RabbitMqJobSource> logger)
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

    private Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        return retryWrapperService.RunAsync(async (state, ct) =>
        {
            // Using previous iteration's exception stored in state to judge whether we need to regenerate the connection and/or channel.
            var regenerateConnection = false;
            var regenerateChannel = false;

            if (state.Exception is OperationInterruptedException
                {
                    ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ConnectionCodeRangeAMin
                    and <= RabbitMqExceptionCodeConstants.ConnectionCodeRangeAMax
                }
                or OperationInterruptedException
                {
                    ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ConnectionCodeRangeBMin
                    and <= RabbitMqExceptionCodeConstants.ConnectionCodeRangeBMax
                })
            {
                regenerateConnection = true;
            }

            if (regenerateConnection || state.Exception is OperationInterruptedException
                {
                    ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ChannelCodeMin
                    and <= RabbitMqExceptionCodeConstants.ChannelCodeMax
                })
            {
                regenerateChannel = true;
            }

            try
            {
                IConnection? connection;
                await _connectionLock.WaitAsync(ct);
                try
                {
                    var connectionWrapper = await connectionCacheSource.GetConnectionAsync(regenerateConnection, ct);
                    if (!connectionWrapper.CachedConnection)
                    {
                        // Fresh connection
                        regenerateChannel = true;
                    }

                    connection = connectionWrapper.Connection;
                }
                finally
                {
                    _connectionLock.Release();
                }

                if (regenerateChannel)
                {
                    _mostRecentChannel = await connection.CreateChannelAsync(cancellationToken: ct);
                }

                await callback(_mostRecentChannel!, cancellationToken);
            }
            catch (Exception e)
            {
                state.Exception = e;
                throw;
            }
        }, new ChannelState
        {
            Exception = null
        }, cancellationToken);
    }
    
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IChannel? _mostRecentChannel;
    
    private sealed class ChannelState
    {
        public required Exception? Exception { get; set; }
    }
    
    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from RabbitMQ Queue: {QueueName}",
            batchSize, configuration.Value.QueueName);

        var getJobsResponseItems = new List<IRawJobModel>();


        while (getJobsResponseItems.Count < batchSize)
        {
            BasicGetResult? result = null;
            await GetChannelAndDoActionWithRetryAsync(async (channel, ct) =>
            {
                result = await channel.BasicGetAsync(configuration.Value.QueueName, false, ct);
            }, cancellationToken);

            /*
             * Historical note: Prior to adding Polly support to RabbitMQ, we used to capture AlreadyClosedException instances
             * thrown when the connection had closed and not been auto-recovered.
             *
             * Modern version wraps this in a WorkerJobSourceException that could be transient.
             * As such, letting the exception bubble up.
             */

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

    public bool IsSubscriptionSource => false;

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

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}