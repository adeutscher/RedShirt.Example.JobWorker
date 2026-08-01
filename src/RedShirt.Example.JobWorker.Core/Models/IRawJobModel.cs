namespace RedShirt.Example.JobWorker.Core.Models;

public interface IRawJobModel
{
    string MessageId { get; }
    string? IdempotencyId { get; }
    string? Body { get; }
    DateTime CreatedAtUtc { get; }
}