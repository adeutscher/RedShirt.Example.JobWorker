using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal class KinesisJobModel : IRawJobModel
{
    public required string ShardId { get; init; }

    /// <summary>
    ///     Within this implementation of a job source, MessageId represents the Kinesis message's sequence number.
    /// </summary>
    public required string MessageId { get; init; }

    public string? IdempotencyId => MessageId;

    public required string? Body { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}