using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

/// <summary>
///     The idempotency monitor is responsible for periodically following up on tasks that were previously blocked due to
///     idempotency issues.
/// </summary>
internal interface IIdempotencyMonitor
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

internal class IdempotencyMonitor(
    IExecutionEndArbiter executionEndArbiter,
    IJobRepository jobRepository,
    IIdempotencyExecutionService idempotencyExecutionService,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    ISleepService sleepService,
    IOptions<IdempotencyConfigurationModel> options) : IIdempotencyMonitor
{
    private async Task CheckBlockedJobsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var blockedJob in await jobRepository.GetAllIdempotencyBlockedJobsAsync(cancellationToken))
        {
            // Job to mark as unblocked.
            IJobRepositoryEntry? unblockedJob = null;

            var idempotencyLock =
                await idempotencyExecutionService.GetLockAsync(blockedJob.JobModel, cancellationToken);
            try
            {
                if (!idempotencyLock.IsAcquired)
                {
                    // Still in motion, proceed to the next entry.
                    continue;
                }

                var cachedResult =
                    await idempotencyExecutionService.GetCachedResultAsync(blockedJob.JobModel, cancellationToken);
                if (cachedResult is null or false)
                {
                    /*
                     * If no cached result, then nothing to acknowledge (did the other worker instance fail to cache it?).
                     * If cached result is false, then this suggests a job source with its own retry mechanism for failed jobs.
                     *
                     * For the running worker instance that currently has the message, this means that it should be re-run.
                     */

                    // Prep to mark as unblocked. See below for justification
                    unblockedJob = blockedJob;
                }
                else
                {
                    // Cached result is true. We are attempting to retry.
                    await safeJobAcknowledgementService.AcknowledgeSafelyAsync(blockedJob, cachedResult.Value,
                        cancellationToken);
                    /*
                     * If the acknowledgment was successful, then the message is complete and can be removed from the repository.
                     * If the acknowledgment was unsuccessful, then this job repository instance has lost custody of the in-flight message.
                     *
                     * In either of these cases, there's nothing more that we can do with this entry in-memory. Remove from repository.
                     */
                    await jobRepository.RemoveJobAsync(blockedJob, cancellationToken);
                }
            }
            finally
            {
                idempotencyLock.Unlock();
            }

            if (unblockedJob is { } jobToUnblock)
            {
                /*
                 * I'm invoking the reload operation in this point in the loop out of fear of an infinite cycle of idempotency monitoring.
                 *
                 * If the job were reloaded within the idempotency lock, then there would be the potential of a race condition.
                 *  If the JobExecutor thread receives job and attempted to acquire an idempotency lock before
                 *  this method's instance of the idempotency lock had a chance to release, then the job would be marked for monitoring.
                 *  That would bring us right back here to CheckBlockedJobsAsync(), and the cycle continues until we win the race condition.
                 *
                 * Instead, we are very deliberately doing this outside of the idempotency lock.
                 */

                await jobRepository.ReloadUnblockedJobAsync(jobToUnblock, cancellationToken);
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            // Not enabled, immediately abort
            return;
        }

        var intervalTimeSpan = TimeSpan.FromSeconds(options.Value.EffectiveMonitorIntervalSeconds);

        while (executionEndArbiter.ShouldKeepRunning())
        {
            await CheckBlockedJobsAsync(cancellationToken);
            await sleepService.DelayAsync(intervalTimeSpan, cancellationToken);
        }
    }
}