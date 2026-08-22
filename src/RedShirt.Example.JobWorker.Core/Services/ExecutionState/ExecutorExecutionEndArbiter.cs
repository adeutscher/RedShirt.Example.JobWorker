using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Dictates if executor workers should continue running.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Originally written as a test-friendly alternative to <c>while(true){}</c>
/// </summary>
internal interface IExecutorExecutionEndArbiter
{
    /// <summary>
    ///     Determine whether executor workers should keep running.
    /// </summary>
    /// <returns><c>true</c> if executors should keep running, otherwise <c>false</c></returns>
    bool ExecutorsShouldKeepRunning();
}

internal sealed class ExecutorExecutionEndArbiter : IExecutorExecutionEndArbiter
{
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly Lock _lock = new();
    private int _idempotencyBlockedJobsCount;

    private int _inactiveJobsCount;

    private void OnInactiveJobCountChange(int inactiveJobCount)
    {
        lock (_lock)
        {
            _inactiveJobsCount = inactiveJobCount;
        }
    }

    private void OnIdempotencyBlockedJobsCountChange(int idempotencyBlockedJobsCount)
    {
        lock (_lock)
        {
            _idempotencyBlockedJobsCount = idempotencyBlockedJobsCount;
        }
    }

    /// <summary>
    ///     Determine whether the monitor should keep running.
    ///     Assumed to be running in a lock statement.
    /// </summary>
    /// <returns><c>true</c> if the monitor should keep running, otherwise <c>false</c></returns>
    private bool ShouldKeepRunningUnsafe()
    {
        return _executionEndArbiter.ShouldKeepRunning()
               // Tracking inactive jobs
               && _inactiveJobsCount > 0
               // Tracking jobs that may become inactive again
               && _idempotencyBlockedJobsCount > 0;
    }

    public ExecutorExecutionEndArbiter(IJobRepository jobRepository, IExecutionEndArbiter executionEndArbiter)
    {
        _executionEndArbiter = executionEndArbiter;
        // Track inactive jobs
        jobRepository.SubscribeToInactiveCountUpdate(OnInactiveJobCountChange);
        // Track jobs that may become inactive again
        jobRepository.SubscribeToIdempotencyBlockedCountUpdate(OnIdempotencyBlockedJobsCountChange);
    }

    public bool ExecutorsShouldKeepRunning()
    {
        lock (_lock)
        {
            return ShouldKeepRunningUnsafe();
        }
    }
}