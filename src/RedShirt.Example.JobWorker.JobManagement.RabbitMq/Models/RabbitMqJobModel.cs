using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

internal class RabbitMqJobModel : IRawJobModel
{
    public required ulong DeliveryTag { get; init; }
    public required string MessageId { get; init; }
    public string? IdempotencyId { get; init; }
    public required string? Body { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}