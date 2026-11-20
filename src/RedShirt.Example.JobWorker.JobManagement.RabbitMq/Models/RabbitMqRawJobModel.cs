using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

internal class RabbitMqRawJobModel : IRawJobModel
{
    public required ulong DeliveryTag { get; init; }

    /// <summary>
    ///     Earlier unsuccessful deliveries of this message, or null when the queue does not track them
    ///     (a redelivered message on a classic queue).
    /// </summary>
    public int? PreviousDeliveryCount { get; init; } = 0;

    public required string MessageId { get; init; }
    public string? IdempotencyId { get; init; }
    public required string? Body { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}