using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Dictates if maintainer workers should continue running.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Written as a test-friendly alternative to `while(true){}`
/// </summary>
internal interface IAppliedMaintainerExecutionEndArbiter
{
    /// <summary>
    ///     Delays for <paramref name="delay" />, honouring both <paramref name="cancellationToken" /> and
    ///     an internal interrupt signal that triggers when the worker is stopping.
    ///     Intended for maintainer workers only.
    ///     Cancellation caused by the internal interrupt signal is ignored and treated as a completed delay.
    /// </summary>
    Task DelayMaintainerWithStopAwarenessAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    bool MaintainerShouldKeepRunning();
}

/// <summary>
///     Dictates if executor workers should continue running.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Written as a test-friendly alternative to `while(true){}`
/// </summary>
internal interface IAppliedExecutorExecutionEndArbiter
{
    bool ExecutorsShouldKeepRunning();
}

internal sealed class AppliedExecutionEndArbiter : IAppliedMaintainerExecutionEndArbiter,
    IAppliedExecutorExecutionEndArbiter, IDisposable
{
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly CancellationTokenSource _interruptCts = new();
    private readonly Lock _lock = new();
    private readonly ISleepService _sleepService;

    private bool _disposed;
    private int _inactiveJobsCount;
    private int _watchedJobsCount;

    /// <summary>
    ///     Centralize decision on whether to send interrupt signal.
    ///     Unsafe on its own, assumed to be running within a lock statement by the method that invokes it.
    /// </summary>
    private bool ShouldSendMaintainerInterruptSignalUnsafe()
    {
        return !_executionEndArbiter.ShouldKeepRunning()
               && _inactiveJobsCount == 0
               && _watchedJobsCount == 0;
    }

    private void TryCancelInterrupt()
    {
        try
        {
            _interruptCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Dispose may have already run (e.g. host shutdown); interrupt signalling is best-effort.
        }
    }

    private void OnInactiveJobChange(int inactiveJobCount)
    {
        bool shouldInterrupt;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _inactiveJobsCount = inactiveJobCount;
            shouldInterrupt = ShouldSendMaintainerInterruptSignalUnsafe();
        }

        if (shouldInterrupt)
        {
            TryCancelInterrupt();
        }
    }

    private void OnWatchedJobChange(int watchedJobCount)
    {
        bool shouldInterrupt;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _watchedJobsCount = watchedJobCount;
            shouldInterrupt = ShouldSendMaintainerInterruptSignalUnsafe();
        }

        if (shouldInterrupt)
        {
            TryCancelInterrupt();
        }
    }

    public AppliedExecutionEndArbiter(
        IExecutionEndArbiter executionEndArbiter,
        IJobRepository jobRepository,
        ISleepService sleepService)
    {
        _executionEndArbiter = executionEndArbiter;
        _sleepService = sleepService;
        jobRepository.SubscribeToInactiveCountUpdate(OnInactiveJobChange);
        jobRepository.SubscribeToWatchedJobsUpdate(OnWatchedJobChange);
    }

    public bool ExecutorsShouldKeepRunning()
    {
        // The executor doesn't care about other executors currently processing jobs, so ignoring the watched jobs count.
        lock (_lock)
        {
            return _executionEndArbiter.ShouldKeepRunning()
                   || _inactiveJobsCount > 0;
        }
    }

    public async Task DelayMaintainerWithStopAwarenessAsync(TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        CancellationToken interruptToken;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            interruptToken = _interruptCts.Token;
        }

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, interruptToken);

        try
        {
            await _sleepService.DelayAsync(delay, linkedCts.Token);
        }
        catch (OperationCanceledException) when (interruptToken.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            // Interrupt-driven cancellation: treat the delay as having elapsed.
        }
    }

    public bool MaintainerShouldKeepRunning()
    {
        lock (_lock)
        {
            return _executionEndArbiter.ShouldKeepRunning()
                   || _inactiveJobsCount > 0
                   || _watchedJobsCount > 0;
        }
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
}