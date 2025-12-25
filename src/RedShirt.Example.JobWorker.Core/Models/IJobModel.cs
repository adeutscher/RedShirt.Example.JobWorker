namespace RedShirt.Example.JobWorker.Core.Models;

/// <summary>
///     Contains message data and metadata.
/// </summary>
public interface IJobModel
{
    string MessageId { get; }
    DateTime CreatedAtUtc { get; }
    IJobDataModel Data { get; }
}