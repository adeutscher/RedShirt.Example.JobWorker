namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

public static class KeyHelper
{
    private static string GetShardWorkerId(string shardName)
    {
        return $"{AppDomain.CurrentDomain.FriendlyName}-{shardName}";
    }

    public static string GetCheckpointKey(string shardName)
    {
        return $"checkpoint:{GetShardWorkerId(shardName)}";
    }

    public static string GetLockKey(string shardName)
    {
        return $"lock:{GetShardWorkerId(shardName)}";
    }
}