namespace RedShirt.Example.JobWorker.Core.Services.Loader;

/// <summary>
///     Dictates if the app should continue running.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Written as a test-friendly alternative to `while(true){}`
/// </summary>
internal interface ILoaderExecutionEndArbiter
{
    Task<bool> ShouldKeepRunningAsync(CancellationToken cancellationToken = default);
}

internal class LoaderExecutionEndArbiter(IExecutionEndArbiter executionEndArbiter, IJobRepository jobRepository)
    : ILoaderExecutionEndArbiter
{
    public async Task<bool> ShouldKeepRunningAsync(CancellationToken cancellationToken = default)
    {
        return executionEndArbiter.ShouldKeepRunning()
               || await jobRepository.GetInactiveJobCountAsync(cancellationToken) > 0;
    }
}