using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Dictates if executor workers should continue running.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Written as a test-friendly alternative to `while(true){}`
/// </summary>
internal interface IExecutorExecutionEndArbiter
{
    bool ExecutorsShouldKeepRunning();
}

internal class ExecutorExecutionEndArbiter : IExecutorExecutionEndArbiter, IDisposable
{
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly CancellationTokenSource _interruptCts = new();
    private readonly Lock _lock = new();
    private bool _disposed;

    private int _inactiveJobsCount;

    private void OnInactiveJobCountChange(int inactiveJobCount)
    {
        lock (_lock)
        {
            _inactiveJobsCount = inactiveJobCount;
        }
    }

    private bool ShouldKeepRunningUnsafe()
    {
        return _executionEndArbiter.ShouldKeepRunning() && _inactiveJobsCount > 0;
    }

    public ExecutorExecutionEndArbiter(IJobRepository jobRepository, IExecutionEndArbiter executionEndArbiter,
        ISleepService sleepService, ILogger<ExecutorExecutionEndArbiter> logger)
    {
        _executionEndArbiter = executionEndArbiter;
        jobRepository.SubscribeToInactiveCountUpdate(OnInactiveJobCountChange);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _interruptCts.Dispose();
    }

    public bool ExecutorsShouldKeepRunning()
    {
        lock (_lock)
        {
            return ShouldKeepRunningUnsafe();
        }
    }
}