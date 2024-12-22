namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

public class CheckpointResponse
{
    public required CheckpointRecord? Checkpoint { get; init; }
}