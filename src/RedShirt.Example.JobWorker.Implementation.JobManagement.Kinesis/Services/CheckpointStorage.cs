using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

public interface ICheckpointStorage
{
    Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default);
    Task UpdateLongTermAsync(string shardName, string iteratorString, CancellationToken cancellationToken = default);
    Task UpdateShortTermAsync(string shardName, string iteratorString, CancellationToken cancellationToken = default);
}

internal class CheckpointStorage(
    IAmazonKinesis kinesis,
    IRedisConnectionSource redisConnectionSource,
    IOptions<KinesisConfiguration> options) : ICheckpointStorage
{
    public async Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default)
    {
        var redis = redisConnectionSource.GetDatabase();

        var shortTermString = await redis.StringGetAsync(KeyHelper.GetCheckpointKey(shardId));
        if (!string.IsNullOrEmpty(shortTermString))
        {
            return shortTermString.ToString();
        }

        // TODO: Get long-term value

        var freshIterator = await kinesis.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamARN = options.Value.StreamArn,
            ShardId = shardId,
            ShardIteratorType = ShardIteratorType.TRIM_HORIZON
        }, cancellationToken);

        return freshIterator.ShardIterator;
    }

    public Task UpdateLongTermAsync(string shardName, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpdateShortTermAsync(string shardName, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        var db = redisConnectionSource.GetDatabase();
        return db.StringSetAsync(KeyHelper.GetCheckpointKey(shardName), iteratorString, TimeSpan.FromHours(1));
    }
}