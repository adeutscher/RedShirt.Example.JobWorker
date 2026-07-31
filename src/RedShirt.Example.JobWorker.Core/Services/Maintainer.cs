using Microsoft.Extensions.Logging;
using Polly;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     The maintainer is responsible for making sure that messages checked out from the job source remain 'in flight'.
/// </summary>
internal interface IMaintainer : IHandlerSubComponent;

internal class Maintainer(
    IHeartbeatCalculator heartbeatCalculator,
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ILogger<Maintainer> logger,
    ISleepService sleepService) : IMaintainer
{
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

    private async Task<TimeSpan> MaintainJobsAsync(List<IJobRepositoryEntry> jobs, CancellationToken cancellationToken)
    {
        TimeSpan? timeToWait = null;

        var retryPolicy = Policy
            .Handle<WorkerJobSourceException>(e => !e.IsCritical && e.IsTransient)
            .RetryAsync(Globals.HeartbeatRetryCount,
                (_, instanceCount) =>
                    sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount)), cancellationToken));

        foreach (var job in jobs)
        {
            // Make sure that we are currently the only thing manipulating this job item.
            var lockId = await job.AcquireLockAsync(cancellationToken);

            try
            {
                if (!job.CanHeartbeat)
                {
                    // Job's flight time cannot be extended further
                    continue;
                }

                if (job.State == JobState.Complete)
                {
                    // Job was completed since it was retrieved. Ignore completely.
                    continue;
                }

                if (!heartbeatCalculator.IsReadyForHeartbeat(job))
                {
                    // Job was refreshed recently
                    var timeToNextHeartbeat1 = heartbeatCalculator.TimeUntilNextHeartbeat(job);
                    timeToWait = !timeToWait.HasValue || timeToNextHeartbeat1 < timeToWait
                        ? timeToNextHeartbeat1
                        : timeToWait;

                    continue;
                }

                logger.LogTrace("Sending heartbeat for message: {MessageId}", job.JobModel.MessageId);

                /*
                 * For the moment, deliberately choosing not to catch globally catch unplanned exceptions.
                 * Unplanned exceptions should absolutely bring down the house,
                 *  as they suggest a fundamental error with the job source implementation
                 * or an unaccounted-for permission configuration issue with the underlying message source.
                 */

                try
                {
                    await retryPolicy.ExecuteAsync(() =>
                        jobSource.HeartbeatAsync(job.JobModel, cancellationToken));
                    job.LastHeartbeatTime = DateTime.UtcNow;
                }
                catch (WorkerJobSourceException e) when (!e.IsCritical)
                {
                    logger.LogWarning(e, "Heartbeat could not be completed for message: {MessageId}",
                        job.JobModel.MessageId);
                    // Assume that if heartbeating failed this time around, then the message will be REALLY expired by the time the next loop iteration comes around.
                    await job.SetIfFlightTimeCanBeExtendedAsync(false, cancellationToken);
                }

                var timeToNextHeartbeat = heartbeatCalculator.TimeUntilNextHeartbeat(job);

                timeToWait = !timeToWait.HasValue || timeToNextHeartbeat < timeToWait
                    ? timeToNextHeartbeat
                    : timeToWait;
            }
            finally
            {
                await job.ReleaseLockAsync(lockId, cancellationToken);
            }
        }

        // Fallback, possibly because all jobs became complete after they were fetched?
        timeToWait ??= TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds);

        if (timeToWait.Value.TotalMilliseconds < MinimumTimeToWaitMilliseconds)
        {
            /*
             * Rounding up to 500ms to avoid possible inching to the next heartbeat check because
             * of what I think is date comparison imprecision, Task.Delay imprecision, or something similar.
             *
             * In local testing, had a situation where the Maintainer slept for 00:00:00.0013576,
             * then for 00:00:00.0002317, and so on for ~15 more times until it finally reached
             * the actual heartbeat threshold.
             *
             * Mitigating that with this block by just waiting for a full half a second. The recommended
             * heartbeat time should not be configured tightly enough for that to be a problem.
             */

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