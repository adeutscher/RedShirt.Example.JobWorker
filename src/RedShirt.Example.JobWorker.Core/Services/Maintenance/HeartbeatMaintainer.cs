using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.Maintenance;

/// <summary>
///     The maintainer is responsible for making sure that messages checked out from the job source remain 'in flight'.
/// </summary>
internal interface IHeartbeatMaintainer : IHandlerSubComponent;

internal class HeartbeatMaintainer(
    IHeartbeatCalculator heartbeatCalculator,
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ILogger<HeartbeatMaintainer> logger,
    ISleepService sleepService) : IHeartbeatMaintainer
{
    /// <summary>
    /// Set the minimum amount of time to sleep for between loops.
    ///
    /// The heartbeat maintainer loop uses this to round values up to 500ms to avoid possible inching
    /// to the next heartbeat check because of what I think is date comparison imprecision,
    /// Task.Delay imprecision, or something similar.
    /// 
    /// In local testing, had a situation where the HeartbeatMaintainer slept for 00:00:00.0013576,
    /// then for 00:00:00.0002317, and so on for ~15 more times until it finally reached
    /// the actual heartbeat threshold.
    ///
    /// Mitigating that issue by setting a minimum. The recommended
    /// heartbeat time should not be configured tightly enough for that to be a problem.
    /// </summary>
    private const int MinimumTimeToWaitMilliseconds = 500;

    /// <summary>
    ///     Minor centralization of a log message
    /// </summary>
    /// <param name="timeToWait"></param>
    /// <param name="cancellationToken"></param>
    private async Task LogAndWaitAsync(TimeSpan timeToWait, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Waiting for {Time} until next heartbeat check", timeToWait);
        await sleepService.DelayAsync(timeToWait, cancellationToken);
    }
    
    /// <summary>
    /// Lazily loaded retry policy.
    /// </summary>
    private AsyncRetryPolicy? _retryPolicy;

    private async Task<TimeSpan?> MaintainJobAsync(IJobRepositoryEntry jobRepositoryEntry,
        CancellationToken cancellationToken)
    {
        _retryPolicy ??= Policy
            .Handle<WorkerJobSourceException>(e => e is {IsCritical: false, IsHandled: false, CouldBeTransient: true})
            .RetryAsync(Globals.HeartbeatRetryCount,
                (_, instanceCount) =>
                    sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount)), cancellationToken));
        
        var lockHandle = await jobRepositoryEntry.AcquireLockAsync(cancellationToken);
        try
        {
            if (!jobRepositoryEntry.CanHeartbeat)
            {
                // Job's flight time cannot be extended further
                return null;
            }

            if (jobRepositoryEntry.State == JobState.Complete)
            {
                // Job was completed since it was retrieved in the list of active jobs from the repository.
                // Ignore completely.
                return null;
            }

            if (!heartbeatCalculator.IsReadyForHeartbeat(jobRepositoryEntry))
            {
                // Job was refreshed recently
                return heartbeatCalculator.TimeUntilNextHeartbeat(jobRepositoryEntry);
            }

            logger.LogTrace("Sending heartbeat for message: {MessageId}", jobRepositoryEntry.JobModel.MessageId);

            /*
             * For the moment, deliberately choosing not to catch globally catch unplanned exceptions.
             * Unplanned exceptions should absolutely bring down the house,
             *  as they suggest a fundamental error with the job source implementation
             * or an unaccounted-for permission configuration issue with the underlying message source.
             */

            try
            {
                await _retryPolicy.ExecuteAsync(() =>
                    jobSource.HeartbeatAsync(jobRepositoryEntry.RawJobModel, cancellationToken));
                jobRepositoryEntry.LastHeartbeatTime = DateTime.UtcNow;
            }
            catch (WorkerJobSourceException e) when (!e.IsCritical)
            {
                logger.LogWarning(e, "Heartbeat could not be completed for message: {MessageId}",
                    jobRepositoryEntry.JobModel.MessageId);
                // Assume that if a heartbeat failed this time around, then the message will be REALLY expired by the time the next loop iteration comes around.
                // The documented recommendation for a heartbeat interval is ~75% of the time until message expiry
                await jobRepositoryEntry.SetIfFlightTimeCanBeExtendedAsync(false, cancellationToken);
            }

            return heartbeatCalculator.TimeUntilNextHeartbeat(jobRepositoryEntry);
        }
        finally
        {
            await jobRepositoryEntry.ReleaseLockAsync(lockHandle, cancellationToken);
        }
    }
    
    private async Task<TimeSpan> MaintainJobsAsync(List<IJobRepositoryEntry> jobs, CancellationToken cancellationToken)
    {
        TimeSpan? timeToWait = null;

        foreach (var jobRepositoryEntry in jobs)
        {
            // Make sure that we are currently the only thing manipulating this job item.
            var lockId = await jobRepositoryEntry.AcquireLockAsync(cancellationToken);

            try
            {
                var jobTimeToWait = await MaintainJobAsync(jobRepositoryEntry, cancellationToken);
                timeToWait = !timeToWait.HasValue || jobTimeToWait < timeToWait
                    ? jobTimeToWait
                    : timeToWait;
            }
            finally
            {
                await jobRepositoryEntry.ReleaseLockAsync(lockId, cancellationToken);
            }
        }

        // Fallback, possibly because all jobs became complete after they were fetched?
        timeToWait ??= TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds);

        if (timeToWait.Value.TotalMilliseconds < MinimumTimeToWaitMilliseconds)
        {
            // Enforcing a floor at MinimumTimeToWaitMilliseconds
            // See comments on MinimumTimeToWaitMilliseconds for more backstory.
            timeToWait = TimeSpan.FromMilliseconds(MinimumTimeToWaitMilliseconds);
        }

        return timeToWait.Value;
    }

    public async Task<HandlerResponseEnum> RunAsync(CancellationToken cancellationToken = default)
    {
        if (jobSource.RecommendedHeartbeatIntervalSeconds <= 0)
        {
            // Not needed by implementation, abort immediately.
            return HandlerResponseEnum.NotEnabled;
        }

        while (await appliedExecutionEndArbiter.MaintainerShouldKeepRunningAsync(cancellationToken))
        {
            var jobs = await jobRepository.GetAllInFlightJobsAsync(cancellationToken);

            if (jobs.Count == 0)
            {
                await LogAndWaitAsync(TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds),
                    cancellationToken);
                continue;
            }

            var timeToWait = await MaintainJobsAsync(jobs, cancellationToken);
            await LogAndWaitAsync(timeToWait, cancellationToken);
        }

        return HandlerResponseEnum.Finished;
    }
}