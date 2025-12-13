using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
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
        await channel.BasicAckAsync(ulong.Parse(message.MessageId), false, cancellationToken);
    }

    public async Task<JobSourceResponse> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var channel = await _channel.Value;

        _logger.LogTrace("Fetching information from RabbitMQ Queue: {QueueName}", _configuration.Value.QueueName);

        var result = await channel.BasicGetAsync(_configuration.Value.QueueName, false, cancellationToken);

        if (result is null)
        {
            return new JobSourceResponse
            {
                RecommendedHeartbeatIntervalSeconds = 0,
                Items = []
            };
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
            // Could not parse message
            return new JobSourceResponse
            {
                RecommendedHeartbeatIntervalSeconds = 0,
                Items = []
            };
        }

        // Got a message

        return new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 0,
            Items =
            [
                new JobModel
                {
                    MessageId = result.DeliveryTag.ToString(),
                    Data = convertedMessage
                }
            ]
        };
    }

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