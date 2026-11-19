namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

/// <summary>
///     Keys for Kinesis checkpoints and shard locks.
///     <para>
///         Keys are <c>checkpoint:{application}:{stream}:{shard}</c> and <c>lock:{application}:{stream}:{shard}</c>.
///         Shard IDs such as <c>shardId-000000000000</c> repeat across streams, so the stream name keeps deployments
///         that share Redis or the DynamoDB table from overwriting each other's checkpoints or holding each other's
///         locks. Colons separate the segments because none of them can contain one.
///     </para>
/// </summary>
internal static class KeyHelper
{
    private static string ApplicationName => AppDomain.CurrentDomain.FriendlyName;

    /// <summary>
    ///     Stream name from a stream ARN (<c>arn:aws:kinesis:{region}:{account}:stream/{name}</c>).
    ///     A value without a slash is treated as the name itself.
    /// </summary>
    internal static string GetStreamName(string streamArn)
    {
        var index = streamArn.LastIndexOf('/');
        return index >= 0 ? streamArn[(index + 1)..] : streamArn;
    }

    private static string GetShardWorkerId(string streamArn, string shardName)
    {
        return $"{ApplicationName}:{GetStreamName(streamArn)}:{shardName}";
    }

    public static string GetCheckpointKey(string streamArn, string shardName)
    {
        return $"checkpoint:{GetShardWorkerId(streamArn, shardName)}";
    }

    public static string GetLockKey(string streamArn, string shardName)
    {
        return $"lock:{GetShardWorkerId(streamArn, shardName)}";
    }

    /// <summary>
    ///     Checkpoint key used before the stream name was added (<c>checkpoint:{application}-{shard}</c>).
    ///     Read only as a fallback, so that existing long-term checkpoints carry over instead of the stream being replayed
    ///     from <c>TRIM_HORIZON</c>. Once every shard has saved a checkpoint under the new key (or the old records have
    ///     expired), this fallback can be removed.
    /// </summary>
    public static string GetLegacyCheckpointKey(string shardName)
    {
        return $"checkpoint:{ApplicationName}-{shardName}";
    }
}
