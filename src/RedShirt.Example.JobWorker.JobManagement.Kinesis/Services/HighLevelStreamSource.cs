using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal class HighLevelStreamSource(
    ICheckpointStorage checkpointStorage,
    IKinesisShardLister lister,
    IAbstractedLocker locker,
    ILowLevelStreamSource lowLevelStreamSource,
    ILogger<HighLevelStreamSource> logger) : IJobSource
{
    private readonly SemaphoreSlim _acknowledgeSemaphore = new(1, 1);

    internal int JobCount { get; set; }
    internal int JobCountTally { get; set; }

    internal string? LastShard { get; set; }
    internal StreamSourceResponse? LastStreamSourceResponse { get; set; }

    internal IAbstractedLock? Lock { get; set; }

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        await _acknowledgeSemaphore.WaitAsync(cancellationToken);
        try
        {
            JobCountTally++;
            if (Lock?.IsAcquired == true && JobCountTally >= JobCount)
            {
                await MoveTrackerAsync(cancellationToken);

                logger.LogTrace("Releasing distributed lock");
                Lock.Unlock();
                Lock = null;
            }
        }
        finally
        {
            _acknowledgeSemaphore.Release();
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        // Clear board
        if (Lock is not null)
        {
            throw new InvalidOperationException("Called again too soon.");
        }

        JobCount = 0;
        JobCountTally = 0;

        // Get Lock Factory

        // List through shards
        var shards = await lister.GetListOfShardsAsync(cancellationToken);
        foreach (var shard in shards)
        {
            // Try to get lock
            var currentIterationLock =
                await locker.GetLockAsync(shard, cancellationToken);

            if (!currentIterationLock.IsAcquired)
            {
                // If cannot get lock, then continue
                // Already in use by another worker
                continue;
            }

            LastShard = shard;

            // Got lock, we now have exclusive access to the shard
            ////
            var iteratorString = await checkpointStorage.GetCheckpointAsync(shard, cancellationToken);

            // Get iterator from storage

            // Get Items
            LastStreamSourceResponse = await lowLevelStreamSource.GetJobsAsync(iteratorString, cancellationToken);

            if (LastStreamSourceResponse.Items.Count == 0)
            {
                await MoveTrackerAsync(cancellationToken);
                // No jobs
                currentIterationLock.Unlock();
                continue;
                // release lock and continue    
            }

            JobCount = LastStreamSourceResponse.Items.Count;
            Lock = currentIterationLock;

            return new JobSourceResponse
            {
                RecommendedHeartbeatIntervalSeconds = 0,
                Items = LastStreamSourceResponse.Items
            };
        }

        return new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 0,
            Items = []
        };
    }

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        // This source does not do heartbeats
        return Task.CompletedTask;
    }

    internal async Task MoveTrackerAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(LastShard) || LastStreamSourceResponse is null)
        {
            return;
        }

        // Update short-term checkpoint
        await checkpointStorage.UpdateShortTermAsync(LastShard, LastStreamSourceResponse.IteratorString,
            cancellationToken);

        // Update long-term checkpoint
        if (!string.IsNullOrWhiteSpace(LastStreamSourceResponse.LastSequenceNumber))
        {
            await checkpointStorage.UpdateLongTermAsync(LastShard, LastStreamSourceResponse.LastSequenceNumber,
                cancellationToken);
        }
    }
}