using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Resolves the queue used for consuming (fixed queue name or a temporary exchange-bound queue).
/// </summary>
internal interface IRabbitMqConsumerQueueService
{
    string ConsumerQueueName { get; }

    Task PrepareConsumerQueueAsync(IChannel channel, CancellationToken cancellationToken);

    void ResetPreparedQueue();
}

internal class RabbitMqConsumerQueueService(
    IOptions<RabbitMqQueueConfigurationModel> options,
    ILogger<RabbitMqConsumerQueueService> logger)
    : IRabbitMqConsumerQueueService
{
    private IChannel? _preparedChannel;
    private string? _temporaryQueueName;

    public string ConsumerQueueName
    {
        get
        {
            var configuration = options.Value;
            if (!configuration.UseExchange)
            {
                if (string.IsNullOrWhiteSpace(configuration.QueueName))
                {
                    throw new InvalidOperationException(
                        "JobSource:RabbitMq:QueueName is required when UseExchange is false.");
                }

                return configuration.QueueName;
            }

            if (string.IsNullOrWhiteSpace(_temporaryQueueName))
            {
                throw new InvalidOperationException(
                    "Temporary consumer queue has not been prepared on the RabbitMQ channel.");
            }

            return _temporaryQueueName;
        }
    }

    public void ResetPreparedQueue()
    {
        _preparedChannel = null;
        _temporaryQueueName = null;
    }

    public async Task PrepareConsumerQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.UseExchange)
        {
            return;
        }

        if (_preparedChannel == channel && !string.IsNullOrWhiteSpace(_temporaryQueueName))
        {
            return;
        }

        ResetPreparedQueue();

        var exchangeName = configuration.TemporaryQueueExchangeName;
        if (string.IsNullOrWhiteSpace(exchangeName))
        {
            throw new InvalidOperationException(
                "JobSource:RabbitMq:TemporaryQueueExchangeName is required when UseExchange is true.");
        }

        await channel.ExchangeDeclarePassiveAsync(exchangeName, cancellationToken);

        var queueName = BuildTemporaryQueueName(configuration.TemporaryQueueNamePrefix);
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: false,
            exclusive: true,
            autoDelete: true,
            passive: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: exchangeName,
            routingKey: string.Empty,
            arguments: new Dictionary<string, object?> {["x-match"] = "all"},
            cancellationToken: cancellationToken);

        _preparedChannel = channel;
        _temporaryQueueName = queueName;

        logger.LogTrace(
            "Prepared temporary RabbitMQ queue {QueueName} bound to exchange {ExchangeName}",
            queueName,
            exchangeName);
    }

    private static string BuildTemporaryQueueName(string? prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(prefix) ? $"jobworker-{suffix}" : $"{prefix}{suffix}";
    }
}
