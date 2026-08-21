using Apache.NMS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal class ActiveMqJobSource(
    IActiveMqConnectionFactory connectionFactory,
    IActiveMqRetryWrapperService retryWrapperService,
    IOptions<ActiveMqJobSource.ConfigurationModel> configuration,
    ILogger<ActiveMqJobSource> logger)
    : IJobSource
{
    private IMessageConsumer? _messageConsumer;

    private async Task<JobSourceResponse> FetchJobsAsync(int batchSize, CancellationToken cancellationToken)
    {
        try
        {
            var consumer = await retryWrapperService.RunAsync(GetConsumerAsync, cancellationToken);
            var getJobsResponseItems = new List<IRawJobModel>();

            while (getJobsResponseItems.Count < batchSize)
            {
                var result =
                    await retryWrapperService.RunAsync(_ => consumer.ReceiveAsync(TimeSpan.FromMilliseconds(100)),
                        cancellationToken);

                if (result is null)
                    // Nothing more to grab at the moment.
                {
                    break;
                }

                // Got a message, add it to return set.
                getJobsResponseItems.Add(new ActiveMqRawJobModel
                {
                    Message = result,
                    MessageId = result.NMSMessageId, // Not really used by this framework, but why not
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            return new JobSourceResponse
            {
                Items = getJobsResponseItems
            };
        }
        catch
        {
            ResetConsumer();
            throw;
        }
    }

    /// <summary>
    ///     Get a cached consumer or get a new one from the connection factory.
    ///     Confirming that the invocation of this method should be already covered by the retry wrapper service.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="CouldNotLoadQueueException"></exception>
    private async Task<IMessageConsumer> GetConsumerAsync(CancellationToken cancellationToken)
    {
        if (_messageConsumer is not null)
        {
            return _messageConsumer;
        }

        var connection = await connectionFactory.GetConnectionAsync(cancellationToken);
        await connection.StartAsync();
        var session = await connection.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge);
        var queue = await session.GetQueueAsync(configuration.Value.QueueName);

        if (queue is null)
        {
            throw new CouldNotLoadQueueException();
        }

        var consumer = await session.CreateConsumerAsync(queue);

        // Cache for later
        _messageConsumer = consumer;

        return consumer;
    }

    private void ResetConsumer()
    {
        _messageConsumer = null;
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public bool IsSubscriptionSource => false;

#pragma warning disable S2325
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not ActiveMqRawJobModel jobModel)
        {
            return;
        }

        // Intentionally not using result
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue
        _ = result;

        // Acknowledge whether successful, recoverable, or unrecoverable
        // (ActiveMQ client API has no direct dead-letter call here).
        await retryWrapperService.RunAsync(
            _ => jobModel.Message.AcknowledgeAsync(),
            cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Max(1, batchSize);

        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from ActiveMQ Queue: {QueueName}",
            batchSize, configuration.Value.QueueName);

        return await FetchJobsAsync(batchSize, cancellationToken);
    }

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IMessage object.
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