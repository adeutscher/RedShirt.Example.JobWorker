using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

/// <summary>
///     The idempotency monitor is responsible for periodically following up on tasks that were previously blocked due to
///     idempotency issues.
/// </summary>
internal interface IIdempotencyMonitor : IHandlerSubComponent;

#pragma warning disable S107
internal sealed class IdempotencyMonitor(
    IExecutionEndArbiter executionEndArbiter,
    IJobRepository jobRepository,
    IIdempotencyExecutionService idempotencyExecutionService,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    ISleepService sleepService,
    IOptions<IdempotencyConfigurationModel> options,
    ICoreStatisticsService coreStatisticsService,
    ILogger<IdempotencyMonitor> logger) : IIdempotencyMonitor
#pragma warning restore S107
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
                if (cachedResult is null || !cachedResult.JobResult.IsSuccessful())
                {
                    /*
                     * If no cached result, then nothing to acknowledge (did the other worker instance fail to cache it?).
                     * If cached result is non-success, then this suggests a job source with its own retry mechanism for failed jobs.
                     *
                     * For the running worker instance that currently has the message, this means that it should be re-run.
                     */

                    // Prep to mark as unblocked. See below for justification on why we don't just call the jobRepository on this line.
                    unblockedJob = blockedJob;
                }
                else
                {
                    // Cached result is successful. We are attempting to retry acknowledgement.
                    var acknowledgeResult = await safeJobAcknowledgementService.AcknowledgeSafelyAsync(
                        blockedJob.RawJobModel,
                        cachedResult.JobResult,
                        null,
                        cachedResult.AcknowledgementResult,
                        cancellationToken);
                    /*
                     * If the acknowledgement was successful, then the message is complete and can be removed from the repository.
                     * If the acknowledgement was unsuccessful, then this job repository instance is no longer the authoritative checkout of the in-flight message.
                     *
                     * In either of these cases, there's not much more that we can do with this entry in-memory. Removing from repository.
                     */
                    logger.LogTrace(
                        "Idempotency Monitor has attempted to acknowledge message {MessageId} . Success: {Success}",
                        blockedJob.JobModel.MessageId, acknowledgeResult.Success);
                    if (acknowledgeResult.Success)
                    {
                        // Above comment aside, if acknowledgement was successful then call SetResult once more.
                        // If messageIds are configured to be considered unique, then current implementation shall
                        //  set cached value to null to attempt to free up cache resources faster
                        await idempotencyExecutionService.SetResultInCacheAsync(blockedJob.RawJobModel,
                            cachedResult.JobResult, cachedResult.AcknowledgementResult, cancellationToken);
                    }

                    coreStatisticsService.RecordResult(cachedResult.JobResult);
                    await jobRepository.RemoveJobAsync(blockedJob, cancellationToken);
                }
            }
            finally
            {
                await idempotencyLock.UnlockAsync(cancellationToken);
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

    public async Task<HandlerComponentResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            // Not enabled, immediately abort
            return HandlerComponentResponse.NotEnabled;
        }

        var intervalTimeSpan = TimeSpan.FromSeconds(options.Value.EffectiveMonitorIntervalSeconds);

        while (executionEndArbiter.ShouldKeepRunning())
        {
            await CheckBlockedJobsAsync(cancellationToken);
            await sleepService.DelayAsync(intervalTimeSpan, cancellationToken);
        }

        return HandlerComponentResponse.Finished;
    }
}