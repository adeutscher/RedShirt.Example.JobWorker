namespace RedShirt.Example.JobWorker.Core.Models;

/// <summary>
///     Contains message data and metadata.
/// </summary>
public interface IJobModel
{
    string MessageId { get; }
    string? IdempotencyId { get; }
    DateTime CreatedAtUtc { get; }
    IJobDataModel Data { get; }
}

public class JobModel : IJobModel
{
    public required string MessageId { get; init; }
    public required string? IdempotencyId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}