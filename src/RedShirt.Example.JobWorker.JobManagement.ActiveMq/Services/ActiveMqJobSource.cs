using Apache.NMS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal class ActiveMqJobSource : IJobSource
{
    private readonly IOptions<ConfigurationModel> _configuration;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IConnection>> _connection;
    private readonly ILogger<ActiveMqJobSource> _logger;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IMessageConsumer>> _messageConsumer;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IQueue?>> _queue;
    private readonly Lazy<Task<ISession>> _session;

    public ActiveMqJobSource(IActiveMqConnectionFactory connectionFactory,
        IOptions<ConfigurationModel> configuration,
        ILogger<ActiveMqJobSource> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connection = new Lazy<Task<IConnection>>(async () =>
        {
            var connection = await connectionFactory.GetConnectionAsync();
            connection.Start();
            return connection;
        });
        _session = new Lazy<Task<ISession>>(async () =>
        {
            var connection = await _connection.Value;
            return await connection.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge);
        });
        _queue = new Lazy<Task<IQueue?>>(async () =>
        {
            var session = await _session.Value;
            return await session.GetQueueAsync(_configuration.Value.QueueName);
        });
        _messageConsumer = new Lazy<Task<IMessageConsumer>>(async () =>
        {
            var queue = await _queue.Value;

            if (queue is null)
            {
                throw new CouldNotLoadQueueException();
            }

            var session = await _session.Value;
            return await session.CreateConsumerAsync(queue);
        });
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

#pragma warning disable S2325
    public Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
#pragma warning restore S2325
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (message is not ActiveMqRawJobModel jobModel)
        {
            return Task.CompletedTask;
        }

        // Intentionally not using result
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue
        _ = result;

        // Acknowledge whether successful, recoverable, or unrecoverable
        // (ActiveMQ client API has no direct dead-letter call here).
        return jobModel.Message.AcknowledgeAsync();
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Max(1, batchSize);

        _logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from ActiveMQ Queue: {QueueName}",
            batchSize, _configuration.Value.QueueName);

        var getJobsResponseItems = new List<IRawJobModel>();

        var consumer = await _messageConsumer.Value;

        while (getJobsResponseItems.Count < batchSize)
        {
            var result = await consumer.ReceiveAsync(TimeSpan.FromMilliseconds(100));

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

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IMessage object.
         */
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}