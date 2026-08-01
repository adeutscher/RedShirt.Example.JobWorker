using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.Maintenance;

/// <summary>
///     The maintainer is responsible for making sure that messages checked out from the job source remain 'in flight'.
/// </summary>
internal interface IHeartbeatMaintainer : IHandlerSubComponent;

internal class HeartbeatMaintainer(
    IHeartbeatCalculator heartbeatCalculator,
    IAppliedExecutionEndArbiter appliedExecutionEndArbiter,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ILogger<HeartbeatMaintainer> logger,
    ISleepService sleepService) : IHeartbeatMaintainer
{
    /// <summary>
    ///     Set the minimum amount of time to sleep for between loops.
    ///     The heartbeat maintainer loop uses this to round values up to 500ms to avoid possible inching
    ///     to the next heartbeat check because of what I think is date comparison imprecision,
    ///     Task.Delay imprecision, or something similar.
    ///     In local testing, had a situation where the HeartbeatMaintainer slept for 00:00:00.0013576,
    ///     then for 00:00:00.0002317, and so on for ~15 more times until it finally reached
    ///     the actual heartbeat threshold.
    ///     Mitigating that issue by setting a minimum. The recommended
    ///     heartbeat time should not be configured tightly enough for that to be a problem.
    /// </summary>
    private const int MinimumTimeToWaitMilliseconds = 500;

    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Minor centralization of a log message
    /// </summary>
    /// <param name="timeToWait"></param>
    /// <param name="cancellationToken"></param>
    private async Task LogAndWaitAsync(TimeSpan timeToWait, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Waiting for {Time} until next heartbeat check", timeToWait);
        await sleepService.DelayAsync(timeToWait, cancellationToken);
    }

    /// <summary>
    ///     Get cached retry pipeline, declaring it if none is currently cached.
    /// </summary>
    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Globals.HeartbeatRetryCount,
                ShouldHandle = new PredicateBuilder()
                    .Handle<WorkerJobSourceException>(e => e is
                        {IsCritical: false, IsHandled: false, CouldBeTransient: true}),
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    // Delay is performed via ISleepService in OnRetry so tests can mock sleeps.
                    // Polly v8 AttemptNumber is 0-based on the failed attempt → 2^0, 2^1, 2^2.
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber)),
                        args.Context.CancellationToken);
                }
            })
            .Build();
    }

    private async Task<TimeSpan?> MaintainJobAsync(IJobRepositoryEntry jobRepositoryEntry,
        CancellationToken cancellationToken)
    {
        if (!jobRepositoryEntry.CanHeartbeat)
        {
            // Job's flight time cannot be extended further
            return null;
        }

        if (jobRepositoryEntry.State == JobState.Complete)
        {
            // Job was completed since it was retrieved in the list of active jobs from the repository.
            // Ignore completely.
            return null;
        }

        if (!heartbeatCalculator.IsReadyForHeartbeat(jobRepositoryEntry))
        {
            // Job was refreshed recently
            return heartbeatCalculator.TimeUntilNextHeartbeat(jobRepositoryEntry);
        }

        logger.LogTrace("Sending heartbeat for message: {MessageId}", jobRepositoryEntry.JobModel.MessageId);

        try
        {
            await GetRetryPipeline().ExecuteAsync(
                async token => await jobSource.HeartbeatAsync(jobRepositoryEntry.RawJobModel, token),
                cancellationToken);
            jobRepositoryEntry.LastHeartbeatTime = DateTime.UtcNow;
        }
        catch (WorkerJobSourceException e) when (!e.IsCritical)
        {
            logger.LogWarning(e, "Heartbeat could not be completed for message: {MessageId}",
                jobRepositoryEntry.JobModel.MessageId);
            // Assume that if a heartbeat failed this time around, then the message will be REALLY expired
            //  by the time the next loop iteration comes around.
            //
            // The documented recommendation for a heartbeat interval is ~75% of the time until message expiry
            await jobRepositoryEntry.SetIfFlightTimeCanBeExtendedAsync(false, cancellationToken);
        }

        return heartbeatCalculator.TimeUntilNextHeartbeat(jobRepositoryEntry);
    }

    private async Task<TimeSpan> MaintainJobsAsync(List<IJobRepositoryEntry> jobs, CancellationToken cancellationToken)
    {
        TimeSpan? timeToWait = null;

        foreach (var jobRepositoryEntry in jobs)
        {
            var jobTimeToWait = await MaintainJobAsync(jobRepositoryEntry, cancellationToken);
            timeToWait = !timeToWait.HasValue || jobTimeToWait < timeToWait
                ? jobTimeToWait
                : timeToWait;
        }

        // Fallback, possibly because all jobs became complete after they were fetched?
        timeToWait ??= TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds);

        if (timeToWait.Value.TotalMilliseconds < MinimumTimeToWaitMilliseconds)
        {
            // Enforcing a floor at MinimumTimeToWaitMilliseconds
            // See comments on MinimumTimeToWaitMilliseconds for more backstory.
            timeToWait = TimeSpan.FromMilliseconds(MinimumTimeToWaitMilliseconds);
        }

        return timeToWait.Value;
    }

    public async Task<HandlerResponseEnum> RunAsync(CancellationToken cancellationToken = default)
    {
        if (jobSource.RecommendedHeartbeatIntervalSeconds <= 0)
        {
            // Not needed by implementation, abort immediately.
            return HandlerResponseEnum.NotEnabled;
        }

        while (await appliedExecutionEndArbiter.MaintainerShouldKeepRunningAsync(cancellationToken))
        {
            var jobs = await jobRepository.GetAllInFlightJobsAsync(cancellationToken);

            if (jobs.Count == 0)
            {
                await LogAndWaitAsync(TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds),
                    cancellationToken);
                continue;
            }

            var timeToWait = await MaintainJobsAsync(jobs, cancellationToken);
            await LogAndWaitAsync(timeToWait, cancellationToken);
        }

        return HandlerResponseEnum.Finished;
    }
}