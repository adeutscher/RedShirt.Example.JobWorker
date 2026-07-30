using Microsoft.Extensions.Logging;
using Polly;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
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
    IJobSource jobSource,
    IJobRepository jobRepository,
    ISafeJobRunner safeJobRunner,
    ISleepService sleepService,
    ILogger<JobExecutor> logger) : IJobExecutor
{
    public async Task RunAsync(int id, CancellationToken cancellationToken = default)
    {
        var acknowledgementRetryPolicy = Policy.Handle<Exception>()
            .RetryAsync(Globals.AcknowledgementRetryCount,
                async (e, instanceCount) =>
                {
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount)),
                        cancellationToken);
                }
            );

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

            try
            {
                await acknowledgementRetryPolicy
                    .ExecuteAsync(() => jobSource.AcknowledgeCompletionAsync(job.JobModel, success, cancellationToken));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Job acknowledge failed");
            }
        }
    }
}