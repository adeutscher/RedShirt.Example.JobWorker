using RedShirt.Example.JobWorker.Common.Services.Utility;
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
    /// <summary>
    ///     Delays for <paramref name="delay" />, honouring both <paramref name="cancellationToken" /> and
    ///     the applied arbiter's interrupt signal.
    ///     Cancellation caused by the interrupt signal is ignored and treated as a completed delay.
    /// </summary>
    Task DelayWithStopAwarenessAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    Task<bool> ExecutorsShouldKeepRunningAsync(CancellationToken cancellationToken = default);
    Task<bool> MaintainerShouldKeepRunningAsync(CancellationToken cancellationToken = default);
}

internal sealed class AppliedExecutionEndArbiter : IAppliedExecutionEndArbiter
{
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly CancellationTokenSource _interruptCts = new();
    private readonly ISleepService _sleepService;

    private int _inactiveJobsCount;
    private int _watchedJobsCount;

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

    private void ConsiderSendingInterruptSignal()
    {
        if (_executionEndArbiter.ShouldKeepRunning() && _inactiveJobsCount == 0 && _watchedJobsCount == 0)
        {
            _interruptCts.Cancel();
        }
    }

    private void OnInactiveJobChange(int inactiveJobCount)
    {
        _inactiveJobsCount = inactiveJobCount;
        ConsiderSendingInterruptSignal();
    }

    private void OnWatchedJobChange(int watchedJobCount)
    {
        _watchedJobsCount = watchedJobCount;
        ConsiderSendingInterruptSignal();
    }

    public async Task DelayWithStopAwarenessAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _interruptCts.Token);

        try
        {
            await _sleepService.DelayAsync(delay, linkedCts.Token);
        }
        catch (OperationCanceledException) when (_interruptCts.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            // Interrupt-driven cancellation: treat the delay as having elapsed.
        }
    }

    public Task<bool> MaintainerShouldKeepRunningAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_executionEndArbiter.ShouldKeepRunning()
                               || _inactiveJobsCount > 0
                               || _watchedJobsCount > 0);
    }

    public Task<bool> ExecutorsShouldKeepRunningAsync(CancellationToken cancellationToken = default)
    {
        // The executor doesn't care about other executors currently processing jobs, so ignoring the watched jobs count.
        return Task.FromResult(_executionEndArbiter.ShouldKeepRunning()
                               || _inactiveJobsCount > 0);
    }
}
