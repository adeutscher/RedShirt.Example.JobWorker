using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Dictates if the app should continue running.
///     Used by Maintainer implementation.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Written as a test-friendly alternative to `while(true){}`
/// </summary>
internal interface IAppliedExecutionEndArbiter
{
    Task<bool> ExecutorsShouldKeepRunningAsync(CancellationToken cancellationToken = default);
    Task<bool> MaintainerShouldKeepRunningAsync(CancellationToken cancellationToken = default);
}

internal sealed class AppliedExecutionEndArbiter(IExecutionEndArbiter executionEndArbiter, IJobRepository jobRepository)
    : IAppliedExecutionEndArbiter
{
    public async Task<bool> MaintainerShouldKeepRunningAsync(CancellationToken cancellationToken = default)
    {
        return executionEndArbiter.ShouldKeepRunning()
               || await jobRepository.GetInactiveJobCountAsync(cancellationToken) > 0
               || await jobRepository.GetWatchedJobsCountAsync(cancellationToken) > 0;
    }

    public async Task<bool> ExecutorsShouldKeepRunningAsync(CancellationToken cancellationToken = default)
    {
        // The executor doesn't care about other executors currently processing jobs, so ignoring the watched jobs count.
        return executionEndArbiter.ShouldKeepRunning()
               || await jobRepository.GetInactiveJobCountAsync(cancellationToken) > 0;
    }
}