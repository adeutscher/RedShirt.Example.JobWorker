using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedLockNet;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal class HighLevelStreamSource(
    ICheckpointStorage checkpointStorage,
    IKinesisShardLister lister,
    IRedisConnectionSource redisConnectionSource,
    ILowLevelStreamSource lowLevelStreamSource,
    ILogger<HighLevelStreamSource> logger,
    IOptions<HighLevelStreamSource.ConfigurationModel> options) : IJobSource
{
    internal readonly SemaphoreSlim AcknowledgeSemaphore = new(1, 1);
    internal int JobCount { get; set; }
    internal int JobCountTally { get; set; }

    internal IRedLock? Lock { get; set; }

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        await AcknowledgeSemaphore.WaitAsync(cancellationToken);
        try
        {
            JobCountTally++;
            if (Lock is not null && JobCount >= JobCountTally)
            {
                logger.LogTrace("Releasing distributed lock");
                Lock.Dispose();
                Lock = null;
            }
        }
        finally
        {
            AcknowledgeSemaphore.Release();
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
        var lockFactory = redisConnectionSource.GetLockFactory();

        // List through shards
        var shards = await lister.GetListOfShardsAsync(cancellationToken);
        foreach (var shard in shards)
        {
            // Try to get lock
            var currentIterationLock =
                await lockFactory.CreateLockAsync(KeyHelper.GetLockKey(shard), TimeSpan.FromSeconds(10));
            if (!currentIterationLock.IsAcquired)
            {
                // If cannot get lock, then continue
                // Already in use by another worker
                continue;
            }

            // Got lock, we now have exclusive access to the shard
            ////
            var iteratorString = await checkpointStorage.GetCheckpointAsync(shard, cancellationToken);

            // Get iterator from storage

            // Get Items
            var innerResponse = await lowLevelStreamSource.GetJobsAsync(iteratorString, cancellationToken);
            // Update short-term checkpoint
            await checkpointStorage.UpdateShortTermAsync(shard, innerResponse.IteratorString, cancellationToken);

            if (innerResponse.Items.Count == 0)
            {
                // No jobs
                currentIterationLock.Dispose(); // Technically done automatically, but to be sure
                continue;
                // release lock and continue    
            }

            JobCount = innerResponse.Items.Count;
            Lock = currentIterationLock;

            // Update long-term checkpoint
            await checkpointStorage.UpdateLongTermAsync(shard, innerResponse.Items.Last().MessageId, cancellationToken);

            return new JobSourceResponse
            {
                RecommendedHeartbeatIntervalSeconds = 0,
                Items = innerResponse.Items
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
        return Task.CompletedTask;
    }

    public class ConfigurationModel
    {
    }
}