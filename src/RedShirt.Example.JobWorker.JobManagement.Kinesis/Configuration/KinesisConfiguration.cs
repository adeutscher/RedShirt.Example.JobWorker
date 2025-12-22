namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;

internal class KinesisConfiguration
{
    public required string StreamArn { get; init; }
    public required bool ShuffleShards { get; init; }
}