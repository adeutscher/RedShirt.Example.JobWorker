using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal class StreamSourceResponse
{
    public required string IteratorString { get; init; }
    public required string? LastSequenceNumber { get; init; }
    public required List<IJobModel> Items { get; init; }
}