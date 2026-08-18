using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

internal class RabbitMqJobSource : IJobSource
{
    private readonly Lazy<Task<IChannel>> _channel;
    private readonly IOptions<ConfigurationModel> _configuration;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Lazy<Task<IConnection>> _connection;
    private readonly ILogger<RabbitMqJobSource> _logger;
    private readonly IRabbitMqRetryWrapperService _retryWrapperService;

    public RabbitMqJobSource(IRabbitMqConnectionFactory connectionFactory,
        IOptions<ConfigurationModel> configuration,
        ILogger<RabbitMqJobSource> logger,
        IRabbitMqRetryWrapperService retryWrapperService)
    {
        _configuration = configuration;
        _logger = logger;
        _retryWrapperService = retryWrapperService;
        _connection = new Lazy<Task<IConnection>>(() => connectionFactory.GetConnectionAsync());
        _channel = new Lazy<Task<IChannel>>(async () =>
        {
            var connection = await _connection.Value;
            return await connection.CreateChannelAsync();
        });
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not RabbitMqJobModel rabbitMqJobModel)
        {
            // Message did not originate from RabbitMQ, return
            return;
        }

        await _retryWrapperService.RunAsync(async ct =>
        {
            var channel = await _channel.Value;
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

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from RabbitMQ Queue: {QueueName}",
            batchSize, _configuration.Value.QueueName);

        return await _retryWrapperService.RunAsync(async ct =>
        {
            var getJobsResponseItems = new List<IRawJobModel>();

            var channel = await _channel.Value;

            while (getJobsResponseItems.Count < batchSize)
            {
                var result = await channel.BasicGetAsync(_configuration.Value.QueueName, false, ct);

                if (result is null)
                    // Nothing more to grab at the moment.
                {
                    break;
                }

                var body = Encoding.UTF8.GetString(result.Body.ToArray());

                // Got a message, add it to return set.
                getJobsResponseItems.Add(new RabbitMqJobModel
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
        }, cancellationToken);
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IConnection object.
         * See: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
         * Since it is not necessary to do any thinking, then it is also not necessary to check that the provided
         * IRawJobDataModel is even a RabbitMqJobModel.
         */
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        public required string QueueName { get; init; }
    }
}