namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;

internal class KinesisConfiguration
{
    public required int BatchSize { get; init; }
    public required string StreamArn { get; init; }
}