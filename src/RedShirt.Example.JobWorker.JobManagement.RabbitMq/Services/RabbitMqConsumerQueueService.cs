using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Resolves the queue used for consuming (fixed queue name or a temporary exchange-bound queue).
/// </summary>
internal interface IRabbitMqConsumerQueueService
{
    string ConsumerQueueName { get; }

    /// <summary>
    ///     Safe queue name for trace logging only. Returns "&lt; Not Declared &gt;" when
    ///     <see cref="ConsumerQueueName" /> would throw. Do not use this value for AMQP operations.
    /// </summary>
    string ConsumerQueueNameForLogging { get; }

    /// <summary>
    ///     Whether jobs are consumed from a temporary queue bound to an exchange.
    /// </summary>
    bool IsUsingExchange { get; }

    /// <summary>
    ///     Exchange name for trace logging when <see cref="IsUsingExchange" /> is true.
    ///     Returns "&lt; Not Declared &gt;" when the exchange name is not configured. For logging only.
    /// </summary>
    string ExchangeNameForLogging { get; }

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

    public bool IsUsingExchange => options.Value.UseExchange;

    public string ConsumerQueueNameForLogging
    {
        get
        {
            try
            {
                return ConsumerQueueName;
            }
            catch (InvalidOperationException)
            {
                return RabbitMqConsumerQueueLoggingConstants.NotDeclaredLoggingPlaceholder;
            }
        }
    }

    public string ExchangeNameForLogging
    {
        get
        {
            var exchangeName = options.Value.TemporaryQueueExchangeName;
            return string.IsNullOrWhiteSpace(exchangeName)
                ? RabbitMqConsumerQueueLoggingConstants.NotDeclaredLoggingPlaceholder
                : exchangeName;
        }
    }

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

        var queueName = BuildTemporaryQueueName(exchangeName, configuration.TemporaryQueueLabel);
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

    private static string BuildTemporaryQueueName(string exchangeName, string? label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        var guid = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(label)
            ? $"{exchangeName}-Temp-{guid}"
            : $"{exchangeName}-Temp-{label}-{guid}";
    }
}
