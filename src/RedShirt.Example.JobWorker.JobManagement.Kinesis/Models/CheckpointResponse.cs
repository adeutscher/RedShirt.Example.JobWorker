namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal class CheckpointResponse
{
    public required CheckpointRecord? Checkpoint { get; init; }
}