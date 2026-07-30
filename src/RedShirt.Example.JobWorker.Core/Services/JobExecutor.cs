using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     The JobExecutor is responsible for continually pulling jobs from the in-memory repository and acting on them.
/// </summary>
internal interface IJobExecutor
{
    /// <summary>
    ///     Begin am executor worker instance.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task RunAsync(int id, CancellationToken cancellationToken = default);
}

internal class JobExecutor(
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    ISafeJobRunner safeJobRunner,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    ILogger<JobExecutor> logger) : IJobExecutor
{
    public async Task RunAsync(int id, CancellationToken cancellationToken = default)
    {
        while (await appliedExecutionEndArbiter.ExecutorsShouldKeepRunningAsync(cancellationToken))
        {
            var job = await jobRepository.GetNextJobAsync(cancellationToken);
            if (job is null)
            {
                // If JobRepository return null, then it implies that the execution end arbiter is about ot return false.
                continue;
            }

            logger.LogTrace("Executor {Id} received message {MessageId}", id, job.JobModel.MessageId);
            var success = await safeJobRunner.RunSafelyAsync(job.JobModel, cancellationToken);
            logger.LogTrace("Executor {Id} finished processing message {MessageId}. Success: {Success}", id,
                job.JobModel.MessageId, success);

            var lockId = await job.AcquireLockAsync(cancellationToken);
            job.State = JobState.Complete;
            await job.ReleaseLockAsync(lockId, cancellationToken);

            await jobRepository.RemoveJobAsync(job, cancellationToken);

            await safeJobAcknowledgementService.AcknowledgeSafelyAsync(job.JobModel, success, cancellationToken);
        }
    }
}