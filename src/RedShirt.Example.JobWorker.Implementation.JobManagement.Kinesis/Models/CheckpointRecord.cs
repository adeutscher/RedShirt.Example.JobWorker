namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

public class CheckpointRecord
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public required string IteratorString { get; init; }
}