namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;

public class RabbitMqQueueConfigurationModel
{
    /// <summary>
    ///     Deliveries allowed for a message that keeps failing with a recoverable failure, when
    ///     <see cref="MaximumDeliveries" /> is not set.
    /// </summary>
    public const int DefaultMaximumDeliveries = 5;

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

    /// <summary>
    ///     Maximum deliveries of a message that keeps failing with a recoverable failure. On the last one, the message
    ///     is rejected without requeue, so the broker dead-letters it if the queue has a dead letter exchange, and
    ///     drops it otherwise.
    ///     Only quorum queues count deliveries (<c>x-delivery-count</c>). Classic queues only mark a message as
    ///     redelivered, so a message on a classic queue is requeued at most once whatever this is set to.
    ///     Unset or not positive means <see cref="DefaultMaximumDeliveries" />.
    /// </summary>
    public int? MaximumDeliveries { get; init; }

    public int EffectiveMaximumDeliveries =>
        MaximumDeliveries is > 0 ? MaximumDeliveries.Value : DefaultMaximumDeliveries;
}
