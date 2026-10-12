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

    /// <summary>
    ///     Optional infix in generated temporary queue names when <see cref="UseExchange" /> is true.
    /// </summary>
    public string? TemporaryQueueLabel { get; init; }
}