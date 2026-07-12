using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

internal class RabbitMqJobSource : IJobSource
{
    private readonly Lazy<Task<IChannel>> _channel;
    private readonly IOptions<ConfigurationModel> _configuration;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IConnection>> _connection;
    private readonly ILogger<RabbitMqJobSource> _logger;
    private readonly ISourceMessageConverter _messageConverter;

    public RabbitMqJobSource(IRabbitMqConnectionFactory connectionFactory,
        IOptions<ConfigurationModel> configuration,
        ISourceMessageConverter messageConverter,
        ILogger<RabbitMqJobSource> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _messageConverter = messageConverter;
        _connection = new Lazy<Task<IConnection>>(() => connectionFactory.GetConnectionAsync());
        _channel = new Lazy<Task<IChannel>>(async () =>
        {
            var connection = await _connection.Value;
            return await connection.CreateChannelAsync();
        });
    }

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channel.Value;
        try
        {
            await channel.BasicAckAsync(ulong.Parse(message.MessageId), false, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            /*
             * An ObjectDispostException implies the closure of the connection underlying
             * the channel that this message originated from.
             *
             * Not considered to be actionable, let the exception pass.
             * The message represented by this message ID already fell back into the queue when the connection died.
             */
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var channel = await _channel.Value;

        _logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from RabbitMQ Queue: {QueueName}",
            batchSize, _configuration.Value.QueueName);

        var getJobsResponseItems = new List<IJobModel>();

        while (getJobsResponseItems.Count < batchSize)
        {
            var result = await channel.BasicGetAsync(_configuration.Value.QueueName, false, cancellationToken);

            if (result is null)
                // Nothing more to grab at the moment.
            {
                break;
            }

            IJobDataModel? convertedMessage = null;
            string? body = null;
            try
            {
                body = Encoding.UTF8.GetString(result.Body.ToArray());
                convertedMessage = _messageConverter.Convert(body);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error parsing RabbitMQ message: {MessageBody}", body);
            }

            if (convertedMessage is null)
            {
                /*
                 * What exactly to do with bad messages is a bit up in the air at the moment.
                 * Deleting them from the queue is 'good enough' for now in this general template.
                 */

                // Delete the message so that it cannot keep gumming up the queue
                await channel.BasicAckAsync(result.DeliveryTag, false, cancellationToken);

                // Try to get a message again.
                continue;
            }

            // Got a message, add it to return set.
            getJobsResponseItems.Add(new JobModel
            {
                MessageId = result.DeliveryTag.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                Data = convertedMessage
            });
        }

        return new JobSourceResponse
        {
            Items = getJobsResponseItems
        };
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IConnection object.
         * See: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
         */
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}