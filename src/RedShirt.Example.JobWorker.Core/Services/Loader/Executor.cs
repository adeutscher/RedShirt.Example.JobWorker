using Microsoft.Extensions.Logging;
using Polly;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.Loader;

/// <summary>
///     The Executor is responsible for pulling and acting on jobs from the in-memory repository.
/// </summary>
internal interface IExecutor
{
    Task RunAsync(int id, CancellationToken cancellationToken = default);
}

internal class Executor(
    ILoaderExecutionEndArbiter loaderExecutionEndArbiter,
    IJobSource jobSource,
    IJobRepository jobRepository,
    ISafeJobRunner safeJobRunner,
    ISleepService sleepService,
    ILogger<Executor> logger) : IExecutor
{
    public async Task RunAsync(int id, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Starting job executor {Id}", id);
        var acknowledgementRetryPolicy = Policy.Handle<Exception>()
            .RetryAsync(Globals.AcknowledgementRetryCount,
                async (e, instanceCount) =>
                {
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount)),
                        cancellationToken);
                }
            );

        while (await loaderExecutionEndArbiter.ExecutorsShouldKeepRunningAsync(cancellationToken))
        {
            logger.LogTrace("Executor {Id} is seeking a message", id);
            var job = await jobRepository.GetNextJobAsync(cancellationToken);
            if (job is null)
            {
                logger.LogTrace("Executor {Id} received null job", id);
                // If JobRepository return null, then it implies that the execution end arbiter is about ot return false.
                continue;
            }

            logger.LogTrace("Executor {Id} received message {MessageId}", id, job.JobModel.MessageId);

            var success = await safeJobRunner.RunSafelyAsync(job.JobModel, cancellationToken);
            logger.LogTrace("Executor {Id} finished processing message {MessageId}. Success: {Success}", id,
                job.JobModel.MessageId, success);

            var lockId = await job.AcquireLockAsync(cancellationToken);
            logger.LogTrace("Executor {Id} acquired lock to mark as Complete", id);
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

        logger.LogTrace("Ending job executor {Id}", id);
    }
}