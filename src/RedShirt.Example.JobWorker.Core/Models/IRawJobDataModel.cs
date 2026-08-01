namespace RedShirt.Example.JobWorker.Core.Models;

public interface IRawJobDataModel
{
    string MessageId { get; }
    string? IdempotencyId { get; }
    string? Body { get; }
    DateTime CreatedAtUtc { get; }
}