using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Utility;

public class KeyHelperTests
{
    private static readonly string ApplicationName = AppDomain.CurrentDomain.FriendlyName;

    [Theory]
    [InlineData("arn:aws:kinesis:us-east-1:000000000000:stream/orders", "orders")]
    [InlineData("arn:aws:kinesis:ca-central-1:123456789012:stream/my.stream-1", "my.stream-1")]
    [InlineData("plain-name", "plain-name")]
    public void GetStreamName_FromArnOrName(string streamArn, string expected)
    {
        Assert.Equal(expected, KeyHelper.GetStreamName(streamArn));
    }

    [Fact]
    public void Keys_DifferAcrossStreamsForSameShard()
    {
        const string shard = "shardId-000000000000";
        const string ordersArn = "arn:aws:kinesis:us-east-1:000000000000:stream/orders";
        const string paymentsArn = "arn:aws:kinesis:us-east-1:000000000000:stream/payments";

        Assert.NotEqual(KeyHelper.GetCheckpointKey(ordersArn, shard), KeyHelper.GetCheckpointKey(paymentsArn, shard));
        Assert.NotEqual(KeyHelper.GetLockKey(ordersArn, shard), KeyHelper.GetLockKey(paymentsArn, shard));
    }

    [Fact]
    public void Keys_IncludeApplicationStreamAndShard()
    {
        const string arn = "arn:aws:kinesis:us-east-1:000000000000:stream/orders";

        Assert.Equal($"checkpoint:{ApplicationName}:orders:shardId-000000000000",
            KeyHelper.GetCheckpointKey(arn, "shardId-000000000000"));
        Assert.Equal($"lock:{ApplicationName}:orders:shardId-000000000000",
            KeyHelper.GetLockKey(arn, "shardId-000000000000"));
    }

    [Fact]
    public void LegacyCheckpointKey_MatchesFormatBeforeStreamName()
    {
        Assert.Equal($"checkpoint:{ApplicationName}-shardId-000000000000",
            KeyHelper.GetLegacyCheckpointKey("shardId-000000000000"));
    }
}