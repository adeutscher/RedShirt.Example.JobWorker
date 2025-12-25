using Apache.NMS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal class ActiveMqJobSource : IJobSource
{
    private readonly IOptions<ConfigurationModel> _configuration;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IConnection>> _connection;
    private readonly ILogger<ActiveMqJobSource> _logger;
    private readonly IActiveMqMessageBodyRetriever _messageBodyRetriever;
    private readonly Lazy<Task<IMessageConsumer>> _messageConsumer;
    private readonly ISourceMessageConverter _messageConverter;
    private readonly ISourceMessageSorter _messageSorter;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IQueue?>> _queue;
    private readonly Lazy<Task<ISession>> _session;

    public ActiveMqJobSource(IActiveMqConnectionFactory connectionFactory,
        IOptions<ConfigurationModel> configuration,
        IActiveMqMessageBodyRetriever messageBodyRetriever,
        ISourceMessageConverter messageConverter,
        ISourceMessageSorter messageSorter,
        ILogger<ActiveMqJobSource> logger)
    {
        _configuration = configuration;
        _messageBodyRetriever = messageBodyRetriever;
        _logger = logger;
        _messageSorter = messageSorter;
        _messageConverter = messageConverter;
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

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is JobModel jobModel)
        {
            await jobModel.Message.AcknowledgeAsync();
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from ActiveMQ Queue: {QueueName}",
            batchSize, _configuration.Value.QueueName);

        var getJobsResponseItems = new List<IJobModel>();

        var consumer = await _messageConsumer.Value;

        while (getJobsResponseItems.Count < batchSize)
        {
            var result = await consumer.ReceiveAsync(TimeSpan.FromMilliseconds(100));

            if (result is null)
            {
                // Nothing more to grab at the moment.
                break;
            }

            IJobDataModel? convertedMessage = null;
            string? body = null;
            try
            {
                body = _messageBodyRetriever.GetMessageBody(result);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    convertedMessage = _messageConverter.Convert(body);
                }
            }
            catch (CouldNotRetrieveMessageBodyException e)
            {
                _logger.LogWarning(e, "Failed to receive message body from {Type}", result.GetType().FullName);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error parsing ActiveMQ message: {MessageBody}", body);
            }

            if (convertedMessage is null)
            {
                /*
                 * What exactly to do with bad messages is a bit up in the air at the moment.
                 * Deleting them from the queue is 'good enough' for now in this general template.
                 */

                // Acknowledge the message so that it cannot keep gumming up the queue
                await result.AcknowledgeAsync();

                // Try to get a message again.
                continue;
            }

            // Got a message, add it to return set.
            getJobsResponseItems.Add(new JobModel
            {
                Message = result,
                MessageId = result.NMSMessageId, // Not really used by this framework, but why not
                CreatedAtUtc = DateTime.UtcNow,
                Data = convertedMessage
            });
        }

        return new JobSourceResponse
        {
            Items = _messageSorter.GetSortedListOfJobs(getJobsResponseItems)
        };
    }

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
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