using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

public class StreamSourceResponse
{
    public required string IteratorString { get; init; }
    public required List<IJobModel> Items { get; init; }
}