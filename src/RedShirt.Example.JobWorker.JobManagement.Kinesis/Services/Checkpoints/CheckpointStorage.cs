using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;

/// <summary>
///     Centralized service for Kinesis checkpoint storage.
/// </summary>
internal interface ICheckpointStorage
{
    /// <summary>
    ///     Attempt to get checkpoint for a shard out of storage.
    ///     If it is not found, then get a fresh shard iterator from Kinesis.
    /// </summary>
    /// <param name="shardId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<string> GetCheckpointAsync(string shardId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Pass-through to long-term sequence-number storage.
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="sequenceNumber"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task UpdateLongTermAsync(string shardName, string sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Pass-through to short-term iterator string storage.
    /// </summary>
    /// <param name="shardName"></param>
    /// <param name="iteratorString"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task UpdateShortTermAsync(string shardName, string iteratorString, CancellationToken cancellationToken = default);
}

internal class CheckpointStorage(
    IShortTermIteratorStorage shortTermIteratorStorage,
    ISequenceNumberStorage sequenceNumberStorage,
    IAmazonKinesis kinesis,
    IKinesisRetryWrapperService retryWrapperService,
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
            var stagedIterator = await retryWrapperService.RunAsync(ct =>
                kinesis.GetShardIteratorAsync(new GetShardIteratorRequest
                {
                    StreamARN = options.Value.StreamArn,
                    ShardId = shardId,
                    ShardIteratorType = ShardIteratorType.AFTER_SEQUENCE_NUMBER,
                    StartingSequenceNumber = sequenceNumber
                }, ct), cancellationToken);

            return stagedIterator.ShardIterator;
        }

        var freshIterator = await retryWrapperService.RunAsync(ct =>
            kinesis.GetShardIteratorAsync(new GetShardIteratorRequest
            {
                StreamARN = options.Value.StreamArn,
                ShardIteratorType = ShardIteratorType.TRIM_HORIZON,
                ShardId = shardId
            }, ct), cancellationToken);

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