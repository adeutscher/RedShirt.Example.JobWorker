using Microsoft.Extensions.Logging;
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
    ILoaderExecutionEndArbiter executionEndArbiter,
    IJobSource jobSource,
    IJobRepository jobRepository,
    ISafeJobRunner safeJobRunner,
    ILogger<Executor> logger) : IExecutor
{
    public async Task RunAsync(int id, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Starting job executor {Id}", id);

        while (await executionEndArbiter.ShouldKeepRunningAsync(cancellationToken))
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

            var lockId = await job.AcquireLockAsync(cancellationToken);
            job.State = JobState.Active;
            await job.ReleaseLockAsync(lockId, cancellationToken);

            var success = await safeJobRunner.RunSafelyAsync(job.JobModel, cancellationToken);
            logger.LogTrace("Executor {Id} finished processing message {MessageId}. Success: {Success}", id,
                job.JobModel.MessageId, success);

            lockId = await job.AcquireLockAsync(cancellationToken);
            logger.LogTrace("Executor {Id} acquired lock to mark as Complete", id);
            job.State = JobState.Complete;
            await job.ReleaseLockAsync(lockId, cancellationToken);

            await jobRepository.RemoveJobAsync(job, cancellationToken);
            await jobSource.AcknowledgeCompletionAsync(job.JobModel, success, cancellationToken);
        }

        logger.LogTrace("Ending job executor {Id}", id);
    }
}