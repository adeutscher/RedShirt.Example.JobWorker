namespace RedShirt.Example.JobWorker.Common.Models;

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