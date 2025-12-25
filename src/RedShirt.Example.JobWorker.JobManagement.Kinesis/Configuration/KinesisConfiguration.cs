namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;

internal class KinesisConfiguration
{
    public required string StreamArn { get; init; }

    /// <summary>
    ///     When listing shards, rotate through which one is the first considered.
    /// </summary>
    public required bool RoundRobinShards { get; init; }

    /// <summary>
    ///     When listing shards, randomize the order to attempt to shard attention more fair.
    /// </summary>
    public required bool ShuffleShards { get; init; }
}