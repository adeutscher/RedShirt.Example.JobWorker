namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;

public class RabbitMqQueueConfigurationModel
{
    /// <summary>
    ///     When true, consume from a temporary queue bound to <see cref="TemporaryQueueExchangeName" />
    ///     instead of <see cref="QueueName" />.
    /// </summary>
    public bool UseExchange { get; init; }

    public string? QueueName { get; init; }

    public string? TemporaryQueueExchangeName { get; init; }

    public string? TemporaryQueueNamePrefix { get; init; }
}