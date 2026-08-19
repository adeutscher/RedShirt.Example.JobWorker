using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal class HighLevelStreamSource(
    ICheckpointStorage checkpointStorage,
    IKinesisShardLister lister,
    IAbstractedLockService lockService,
    ILowLevelStreamSource lowLevelStreamSource,
    IKinesisRetryWrapperService retryWrapperService,
    ILogger<HighLevelStreamSource> logger) : IJobSource
{
    internal readonly Dictionary<string, KinesisTrackerSession> Sessions = new();
    private readonly SemaphoreSlim _sessionsSemaphore = new(1, 1);

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not KinesisJobModel kinesisJobModel)
        {
            return;
        }

        // result is intentionally unused for shard checkpointing: the stream always advances.
        // Unrecoverable failures are handled via IJobFailureHandler (application DLQ).
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue 
        _ = result;

        await _sessionsSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (!Sessions.TryGetValue(kinesisJobModel.ShardId, out var trackerSession))
            {
                return;
            }

            trackerSession.Increment(kinesisJobModel.MessageId);

            if (trackerSession.IsComplete)
            {
                /*
                 * Handling MoveTrackerAsync and ReleaseLockOnShardAsync within the semaphore claim
                 * technically bottlenecks operation behind Redis/DynamoDB latency.
                 *
                 * However, if we did not do this within the semaphore claim then we would create the potential for multiple invocations of this method to remove/unlock a single session.
                 * Between the two, the chance of a slight delay is preferred to the chance of a double execution.
                 * A delay is a speed bump, but a double-execution raises serious operational concerns.
                 */
                await MoveTrackerAsync(trackerSession, cancellationToken);
                logger.LogTrace("Releasing distributed lock");
                // Releasing distributed lock so that GetJobsAsync calls can poll this shard again
                await trackerSession.ReleaseLockOnShardAsync(cancellationToken);
                Sessions.Remove(kinesisJobModel.ShardId);
            }
        }
        finally
        {
            _sessionsSemaphore.Release();
        }
    }

    /// <summary>
    ///     Kinesis is a stream rather than a traditional message broker, and has no heartbeats to consider an interval for.
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds => 0;

    public bool IsSubscriptionSource => false;

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        // List through shards
        var shards = await lister.GetListOfShardsAsync(cancellationToken);
        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var shard in shards)
        {
            await _sessionsSemaphore.WaitAsync(cancellationToken);
            try
            {
                /*
                 * Later logic within this loop is storing sessions with acquired locks for this process.
                 * Therefore, we can safely assume that we would have been unable to acquire a lock on a shard if an entry for it exists in the session dictionary.
                 * Doing this locally avoids making an additional Redis call for a lock that we know that we will be unable to acquire.
                 */
                if (Sessions.ContainsKey(shard))
                {
                    continue;
                }
            }
            finally
            {
                _sessionsSemaphore.Release();
            }

            // Try to get lock
            var currentIterationLock = await retryWrapperService.RunAsync(
                ct => lockService.GetLockAsync(KeyHelper.GetLockKey(shard), ct),
                cancellationToken);

            StreamSourceResponse? lowLevelStreamResponse;
            var sessionIsStored = false;
            try
            {
                if (!currentIterationLock.IsAcquired)
                {
                    // If the worker cannot get a lock, then continue
                    // Already in use by another instance of this worker
                    continue;
                }

                // Got lock, we now have exclusive access to the shard

                // Get iterator from storage
                var iteratorString = await checkpointStorage.GetCheckpointAsync(shard, cancellationToken);

                // Get Items
                lowLevelStreamResponse =
                    await lowLevelStreamSource.GetJobsAsync(batchSize, shard, iteratorString, cancellationToken);

                // Compile into one 'tracker session' object
                var trackerSession = new KinesisTrackerSession(shard, lowLevelStreamResponse, currentIterationLock);

                if (trackerSession.Count == 0)
                {
                    await MoveTrackerAsync(trackerSession, cancellationToken);
                    // No jobs, so release continue (lock will be released by try-finally    
                    continue;
                }

                try
                {
                    // Register
                    await _sessionsSemaphore.WaitAsync(cancellationToken);
                    /*
                     * Note: This session-storage approach technically leaks locks by design.
                     *
                     * If you are concerned because of concerns that an AI audit brought up,
                     * the answer would be that it's not entirely off-base.
                     *
                     * This system of locking ("leaking" and all) is essential to avoiding an outside-context problem such as a flavour of sudden container death causing messages to be fully lost to any instance of this application.
                     * It is an intentional safety-mechanism.
                     *
                     * Because of this, the implementation of Kinesis as a job source is even more reliant than other job sources on being invoked as designed at the Core project level.
                     */
                    Sessions[shard] = trackerSession;
                    sessionIsStored = true;
                }
                finally
                {
                    _sessionsSemaphore.Release();
                }
            }
            finally
            {
                // Only unlock the current iteration if the session was not stored for later.
                // If it was stored for later, then that suggests that the session will be unlocked
                // in AcknowledgeAsync once all the records in the session have been acknowledged. 
                if (!sessionIsStored)
                {
                    await currentIterationLock.UnlockAsync(cancellationToken);
                }
            }

            return new JobSourceResponse
            {
                Items = lowLevelStreamResponse.Items.ToList()
            };
        }

        // Fell through foreach, did not find any shards with records.
        return new JobSourceResponse
        {
            Items = []
        };
    }

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        // This source does not do heartbeats
        return Task.CompletedTask;
    }

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public void StopSubscriber()
    {
        throw new NotSupportedException();
    }

    /// <summary>
    ///     Update trackers.
    /// </summary>
    /// <param name="kinesisTrackerSession"></param>
    /// <param name="cancellationToken"></param>
    internal async Task MoveTrackerAsync(KinesisTrackerSession kinesisTrackerSession,
        CancellationToken cancellationToken)
    {
        /*
         * Because pulling data is sequential and execution could be in an arbitrary order, this is our only way forward with the Kinesis technology.
         *
         * Under the Kinesis model, a failed message in a batch is the responsibility of the SqsQueueFailureHandler implementation of IJobFailureHandler.
         * This is another case where the AI-driven audit wasn't entirely wrong, but it does not understand the full context.
         */

        // Update short-term checkpoint
        await checkpointStorage.UpdateShortTermAsync(kinesisTrackerSession.ShardName,
            kinesisTrackerSession.StreamSourceResponse.IteratorString, cancellationToken);

        // Update long-term checkpoint
        if (!string.IsNullOrWhiteSpace(kinesisTrackerSession.StreamSourceResponse.LastSequenceNumber))
        {
            await checkpointStorage.UpdateLongTermAsync(kinesisTrackerSession.ShardName,
                kinesisTrackerSession.StreamSourceResponse.LastSequenceNumber, cancellationToken);
        }
    }
}