using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;

namespace RedShirt.Example.JobWorker.Core.Services;

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
    Task<HandlerResponseEnum> RunAsync(int executorId, CancellationToken cancellationToken = default);
}

internal class JobExecutor(
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    IIdempotencyExecutionService idempotencyExecutionService,
    ISafeJobRunner safeJobRunner,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    ILogger<JobExecutor> logger) : IJobExecutor
{
    private async Task ActOnJobAsync(int executorId, IJobRepositoryEntry repositoryEntry,
        CancellationToken cancellationToken = default)
    {
        var cachedAttemptResult =
            await idempotencyExecutionService.GetCachedResultAsync(repositoryEntry.JobModel, cancellationToken);
        if (cachedAttemptResult?.JobSuccess == true)
        {
            var idempotentAcknowledgementReport =
                await safeJobAcknowledgementService.AcknowledgeSafelyAsync(
                    repositoryEntry.RawJobModel,
                    true,
                    previousAttempt: null,
                    exception: null,
                    cancellationToken: cancellationToken);
            if (!idempotentAcknowledgementReport.Success)
            {
                /*
                 * An unsuccessful acknowledgement suggests that the executor somehow managed to lose custody of a message
                 * within a split second of receiving it. Nothing we can do except to log it, continue.
                 */
                logger.LogError("Executor {Id} failed to acknowledge cached result for message {MessageId}", executorId,
                    repositoryEntry.JobModel.MessageId);
            }

            // Send a notice to the idempotency execution service to refresh/remove the cache entry (depending on downstream settings) 
            await idempotencyExecutionService.SetResultInCacheAsync(repositoryEntry.RawJobModel, true,
                idempotentAcknowledgementReport, cancellationToken);
            return;
        }

        /*
         * If the idempotency cache returned null, then there is no proof of a previous attempt. If so, then we need to run the task for the first time.
         * If the idempotency cache returned false, then assume that we need to retry
         */

        var safeJobResult = await safeJobRunner.RunSafelyAsync(repositoryEntry.JobModel, cancellationToken);
        logger.LogTrace("Executor {Id} finished processing message {MessageId}. Success: {Success}", executorId,
            repositoryEntry.JobModel.MessageId, safeJobResult.JobSuccess);

        await repositoryEntry.SetStateAsync(JobState.Complete, cancellationToken);

        await jobRepository.RemoveJobAsync(repositoryEntry, cancellationToken);

        var acknowledgementSuccess =
            await safeJobAcknowledgementService.AcknowledgeSafelyAsync(repositoryEntry.RawJobModel,
                safeJobResult.JobSuccess,
                // There is no previous attempt to this particular invocation of ISafeJobRunner
                previousAttempt: null,
                exception: safeJobResult.JobSuccess ? null : safeJobResult.Exception,
                cancellationToken: cancellationToken);
        await idempotencyExecutionService.SetResultInCacheAsync(repositoryEntry.RawJobModel, safeJobResult.JobSuccess,
            acknowledgementSuccess,
            cancellationToken);
    }

    public async Task<HandlerResponseEnum> RunAsync(int executorId, CancellationToken cancellationToken = default)
    {
        while (await appliedExecutionEndArbiter.ExecutorsShouldKeepRunningAsync(cancellationToken))
        {
            var job = await jobRepository.GetNextJobAsync(cancellationToken);
            if (job is null)
            {
                // If JobRepository return null, then it implies that the execution end arbiter is about ot return false.
                continue;
            }

            logger.LogTrace("Executor {Id} received message {MessageId}", executorId, job.JobModel.MessageId);

            var idempotencyLock = await idempotencyExecutionService.GetLockAsync(job.JobModel, cancellationToken);

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
                        executorId, job.JobModel.MessageId);
                    await job.SetStateAsync(JobState.BlockedByIdempotency, cancellationToken);
                    continue;
                }

                await ActOnJobAsync(executorId, job, cancellationToken);
            }
            finally
            {
                await idempotencyLock.UnlockAsync(cancellationToken);
            }
        }

        return HandlerResponseEnum.Finished;
    }
}