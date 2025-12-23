using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.Loader;

internal interface IMaintainer
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

internal class Maintainer(
    IHeartbeatCalculator heartbeatCalculator,
    ILoaderExecutionEndArbiter executionEndArbiter,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ILogger<Maintainer> logger) : IMaintainer
{
    /// <summary>
    ///     Minor centralization of a log message
    /// </summary>
    /// <param name="timeToWait"></param>
    /// <param name="cancellationToken"></param>
    private async Task LogAndWaitAsync(TimeSpan timeToWait, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Waiting for {Time} until next heartbeat check", timeToWait);
        await Task.Delay(timeToWait, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (await executionEndArbiter.ShouldKeepRunningAsync(cancellationToken))
        {
            var jobs = await jobRepository.GetAllInFlightJobsAsync(cancellationToken);

            if (jobs.Count == 0)
            {
                await LogAndWaitAsync(TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds),
                    cancellationToken);
                continue;
            }

            TimeSpan? timeToWait = null;

            foreach (var job in jobs)
            {
                // Make sure that we are currently the only thing manipulating this job item.
                var lockId = await job.AcquireLockAsync(cancellationToken);

                if (job.State == JobState.Complete)
                {
                    // Job was completed since it was retrieved. Ignore completely.
                    await job.ReleaseLockAsync(lockId, cancellationToken);
                    continue;
                }

                if (!heartbeatCalculator.IsReadyForHeartbeat(job))
                {
                    // Job was refreshed recently
                    var timeToNextHeartbeat1 = heartbeatCalculator.TimeUntilNextHeartbeat(job);
                    timeToWait = !timeToWait.HasValue || timeToNextHeartbeat1 < timeToWait
                        ? timeToNextHeartbeat1
                        : timeToWait;

                    await job.ReleaseLockAsync(lockId, cancellationToken);
                    continue;
                }

                logger.LogTrace("Sending heartbeat for message: {MessageId}", job.JobModel.MessageId);
                await jobSource.HeartbeatAsync(job.JobModel, cancellationToken);
                job.LastHeartbeatTime = DateTime.UtcNow;

                var timeToNextHeartbeat = heartbeatCalculator.TimeUntilNextHeartbeat(job);
                timeToWait = !timeToWait.HasValue || timeToNextHeartbeat < timeToWait
                    ? timeToNextHeartbeat
                    : timeToWait;

                await job.ReleaseLockAsync(lockId, cancellationToken);
            }

            // Fallback, possibly because all jobs became complete after they were fetched?
            timeToWait ??= TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds);

            if (timeToWait.Value.TotalMilliseconds < 500)
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

                timeToWait = TimeSpan.FromMilliseconds(500);
            }

            await LogAndWaitAsync(timeToWait.Value, cancellationToken);
        }
    }
}