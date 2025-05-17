namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

public class CheckpointResponse
{
    public required CheckpointRecord? Checkpoint { get; init; }
}