using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs;

/// <summary>
///     The JobExecutor is responsible for continually pulling jobs from the in-memory repository and acting on them.
/// </summary>
internal interface IJobExecutor
{
    /// <summary>
    ///     Run an executor worker instance.
    /// </summary>
    /// <param name="executorId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<HandlerComponentResponse> RunAsync(int executorId, CancellationToken cancellationToken = default);
}

internal sealed class JobExecutor(
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    IIdempotencyExecutionService idempotencyExecutionService,
    ISafeJobRunner safeJobRunner,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    ICoreStatisticsService coreStatisticsService,
    ILogger<JobExecutor> logger) : IJobExecutor
{
    private async Task ActOnJobAsync(int executorId, IJobRepositoryEntry repositoryEntry,
        CancellationToken cancellationToken = default)
    {
        var cachedAttemptResult =
            await idempotencyExecutionService.GetCachedResultAsync(repositoryEntry.JobModel, cancellationToken);
        if (cachedAttemptResult is {JobResult: var cachedResult} && cachedResult.IsSuccessful())
        {
            var newIdempotentAcknowledgementReport =
                await safeJobAcknowledgementService.AcknowledgeSafelyAsync(
                    repositoryEntry.RawJobModel,
                    cachedResult,
                    previousAttempt: null,
                    exception: null,
                    cancellationToken: cancellationToken);
            if (!newIdempotentAcknowledgementReport.Success)
            {
                /*
                 * An unsuccessful acknowledgement suggests that the executor somehow managed to lose custody of a message
                 * within a split second of receiving it. Nothing we can do except to log it, continue.
                 */
                logger.LogError("Executor {Id} failed to acknowledge cached result for message {MessageId}", executorId,
                    repositoryEntry.JobModel.MessageId);
            }

            // Send a notice to the idempotency execution service to refresh/remove the cache entry (depending on downstream settings)
            await idempotencyExecutionService.SetResultInCacheAsync(repositoryEntry.RawJobModel, cachedResult,
                newIdempotentAcknowledgementReport, cancellationToken);
            coreStatisticsService.RecordResult(cachedResult);
            return;
        }

        /*
         * If the idempotency cache returned null, then there is no proof of a previous attempt. If so, then we need to run the task for the first time.
         * If the idempotency cache returned a non-success result, then assume that we need to retry
         */

        var stopwatch = Stopwatch.StartNew();
        var safeJobResult = await safeJobRunner.RunSafelyAsync(repositoryEntry.JobModel, cancellationToken);
        stopwatch.Stop();

        /*
         * Update statistics.
         * Acknowledging that this could have an impact on how statistics could be interpreted.
         * A dropped message that was caught by the idempotency system as only needing to be acknowledged technically isn't executed, and this could technically result in a higher statistic of successful jobs.
         * The counter-argument is an appeal to simplicity. Accounting for idempotency details just isn't a priority at the moment, and I feel like to do so would be added complexity.
         */
        coreStatisticsService.RecordResult(safeJobResult.Result, stopwatch.Elapsed);

        logger.LogTrace("Executor {Id} finished processing message {MessageId}. Result: {Result}", executorId,
            repositoryEntry.JobModel.MessageId, safeJobResult.Result);

        var acknowledgementSuccess =
            await safeJobAcknowledgementService.AcknowledgeSafelyAsync(repositoryEntry.RawJobModel,
                safeJobResult.Result,
                // There is no previous attempt to this particular invocation of ISafeJobRunner
                previousAttempt: null,
                exception: safeJobResult.Result.IsSuccessful() ? null : safeJobResult.Exception,
                cancellationToken: cancellationToken);
        await idempotencyExecutionService.SetResultInCacheAsync(repositoryEntry.RawJobModel, safeJobResult.Result,
            acknowledgementSuccess,
            cancellationToken);
    }

    public async Task<HandlerComponentResponse> RunAsync(int executorId, CancellationToken cancellationToken = default)
    {
        while (await appliedExecutionEndArbiter.ExecutorsShouldKeepRunningAsync(cancellationToken))
        {
            var repositoryEntry = await jobRepository.GetNextJobAsync(cancellationToken);
            if (repositoryEntry is null)
            {
                // If JobRepository return null, then it implies that the execution end arbiter is about ot return false.
                continue;
            }

            logger.LogTrace("Executor {Id} received message {MessageId}", executorId,
                repositoryEntry.JobModel.MessageId);

            var idempotencyLock =
                await idempotencyExecutionService.GetLockAsync(repositoryEntry.JobModel, cancellationToken);

            try
            {
                if (!idempotencyLock.IsAcquired)
                {
                    /*
                     * A failure to get a lock suggests that idempotency has been enabled and that an instance of the job is actively running.
                     *
                     * This JobExecutor run should mark this instance of the job retrieval as being blocked, and proceed to try pulling another job.
                     *
                     * Another process will follow up on the blocked jobs.
                     */
                    logger.LogTrace(
                        "Executor {Id} was unable to obtain a lock on message {MessageId} , deferring to Idempotency Monitor",
                        executorId, repositoryEntry.JobModel.MessageId);
                    await repositoryEntry.SetStateAsync(JobState.BlockedByIdempotency, cancellationToken);
                    continue;
                }

                await ActOnJobAsync(executorId, repositoryEntry, cancellationToken);

                // Mark as complete for all branches of ActOnJobAsync by doing it afterwards
                // Reminder that JobState does not imply anything about success or acknowledgement success.
                // It only means that the JobWorker is done with the job.
                await repositoryEntry.SetStateAsync(JobState.Complete, cancellationToken);
                await jobRepository.RemoveJobAsync(repositoryEntry, cancellationToken);
            }
            finally
            {
                await idempotencyLock.UnlockAsync(cancellationToken);
            }
        }

        return HandlerComponentResponse.Finished;
    }
}