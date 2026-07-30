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
    Task RunAsync(int executorId, CancellationToken cancellationToken = default);
}

internal class JobExecutor(
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    IIdempotencyExecutionService idempotencyExecutionService,
    ISafeJobRunner safeJobRunner,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    ILogger<JobExecutor> logger) : IJobExecutor
{
    private async Task ActOnJobAsync(int executorId, IJobRepositoryEntry job,
        CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Executor {Id} received message {MessageId}", executorId, job.JobModel.MessageId);

        var cachedIdempotentResult =
            await idempotencyExecutionService.GetCachedResultAsync(job.JobModel, cancellationToken);
        if (cachedIdempotentResult == true)
        {
            var idempotentAcknowledgementSuccess =
                await safeJobAcknowledgementService.AcknowledgeSafelyAsync(job.JobModel, true, cancellationToken);
            if (!idempotentAcknowledgementSuccess)
            {
                /*
                 * Implies that the executor somehow managed to lose custody of a message within a split second of receiving it.
                 * Nothing we can do, continue.
                 */
                logger.LogError("Executor {Id} failed to acknowledge cached result for message {MessageId}", executorId,
                    job.JobModel.MessageId);
                return;
            }
        }

        /*
         * If the idempotency cache returned null, then there is no proof of a previous attempt. If so, then we need to run the task for the first time.
         * If the idempotency cache returned false, then assume that we need to retry
         */

        var success = await safeJobRunner.RunSafelyAsync(job.JobModel, cancellationToken);
        logger.LogTrace("Executor {Id} finished processing message {MessageId}. Success: {Success}", executorId,
            job.JobModel.MessageId, success);

        await job.SetStateAsync(JobState.Complete, cancellationToken);

        await jobRepository.RemoveJobAsync(job, cancellationToken);

        var acknowledgementSuccess =
            await safeJobAcknowledgementService.AcknowledgeSafelyAsync(job.JobModel, success, cancellationToken);
        await idempotencyExecutionService.SetResultInCacheAsync(job.JobModel, success, acknowledgementSuccess,
            cancellationToken);
    }

    public async Task RunAsync(int executorId, CancellationToken cancellationToken = default)
    {
        while (await appliedExecutionEndArbiter.ExecutorsShouldKeepRunningAsync(cancellationToken))
        {
            var job = await jobRepository.GetNextJobAsync(cancellationToken);
            if (job is null)
            {
                // If JobRepository return null, then it implies that the execution end arbiter is about ot return false.
                continue;
            }

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
                    await job.SetStateAsync(JobState.BlockedByIdempotency, cancellationToken);
                    continue;
                }

                await ActOnJobAsync(executorId, job, cancellationToken);
            }
            finally
            {
                idempotencyLock.Unlock();
            }
        }
    }
}