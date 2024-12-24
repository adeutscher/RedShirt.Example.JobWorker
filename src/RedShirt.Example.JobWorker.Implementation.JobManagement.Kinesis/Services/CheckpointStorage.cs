using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

public interface ICheckpointStorage
{
    Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default);
    Task UpdateLongTermAsync(string shardName, string sequenceNumber, CancellationToken cancellationToken = default);
    Task UpdateShortTermAsync(string shardName, string iteratorString, CancellationToken cancellationToken = default);
}

internal class CheckpointStorage(
    IShortTermIteratorStorage shortTermIteratorStorage,
    ISequenceNumberStorage sequenceNumberStorage,
    IAmazonKinesis kinesis,
    IOptions<KinesisConfiguration> options) : ICheckpointStorage
{
    public async Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default)
    {
        var shortTermString = await shortTermIteratorStorage.GetAsync(shardId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(shortTermString))
        {
            return shortTermString;
        }

        var sequenceNumber =
            await sequenceNumberStorage.GetLastSequenceNumber(shardId, cancellationToken);
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
        return sequenceNumberStorage.SetLastSequenceNumber(shardName, sequenceNumber,
            cancellationToken);
    }

    public Task UpdateShortTermAsync(string shardName, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        return shortTermIteratorStorage.SetAsync(shardName, iteratorString, cancellationToken);
    }
}