using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

public interface ICheckpointStorage
{
    Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default);
    Task UpdateLongTermAsync(string shardName, string sequenceNumber, CancellationToken cancellationToken = default);
    Task UpdateShortTermAsync(string shardName, string iteratorString, CancellationToken cancellationToken = default);
}

internal class CheckpointStorage(
    IRedisConnectionSource redisConnectionSource,
    ISequenceNumberStorage sequenceNumberStorage,
    IAmazonKinesis kinesis,
    IOptions<KinesisConfiguration> options) : ICheckpointStorage
{
    public async Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default)
    {
        var redis = redisConnectionSource.GetDatabase();

        var shortTermString = await redis.StringGetAsync(KeyHelper.GetCheckpointKey(shardId));
        if (!string.IsNullOrWhiteSpace(shortTermString))
        {
            return shortTermString.ToString();
        }

        var sequenceNumber =
            await sequenceNumberStorage.GetLastSequenceNumber(KeyHelper.GetCheckpointKey(shardId), cancellationToken);
        if (!string.IsNullOrEmpty(sequenceNumber))
        {
            var stagedIterator = await kinesis.GetShardIteratorAsync(new GetShardIteratorRequest
            {
                StreamARN = options.Value.StreamArn,
                ShardId = shardId,
                ShardIteratorType = ShardIteratorType.AFTER_SEQUENCE_NUMBER,
                StartingSequenceNumber = sequenceNumber
            }, cancellationToken);

            return stagedIterator.ShardIterator;
        }

        var freshIterator = await kinesis.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamARN = options.Value.StreamArn,
            ShardIteratorType = ShardIteratorType.TRIM_HORIZON,
            ShardId = shardId
        }, cancellationToken);

        return freshIterator.ShardIterator;
    }

    public Task UpdateLongTermAsync(string shardName, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        return sequenceNumberStorage.SetLastSequenceNumber(KeyHelper.GetCheckpointKey(shardName), sequenceNumber,
            cancellationToken);
    }

    public Task UpdateShortTermAsync(string shardName, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        var db = redisConnectionSource.GetDatabase();
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (string.IsNullOrEmpty(iteratorString))
        {
            return db.StringSetAsync(KeyHelper.GetCheckpointKey(shardName), string.Empty, TimeSpan.FromSeconds(1));
        }

        return db.StringSetAsync(KeyHelper.GetCheckpointKey(shardName), iteratorString,
            TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5));
    }
}