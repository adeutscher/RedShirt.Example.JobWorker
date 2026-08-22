using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Dictates if the idempotency monitor should continue running.
///     Extends the functionality of the base IExecutionEndArbiter by accessing the job repository.
///     Originally written as a test-friendly alternative to <c>while(true){}</c>
/// </summary>
internal interface IIdempotencyMonitorExecutionEndArbiter : IDisposable
{
    /// <summary>
    ///     Delays for <paramref name="delay" />, honouring both <paramref name="cancellationToken" /> and
    ///     an internal interrupt signal that triggers when the worker is stopping.
    ///     If there are no blocked jobs for the idempotency monitor to observe, then also delays until there are
    ///     before delaying for <paramref name="delay" />.
    ///     Cancellation caused by the internal interrupt signal is ignored and treated as a completed delay.
    /// </summary>
    /// <param name="delay">How long to wait when the skip-wait event is not set.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    Task IdempotencyMonitorDelayWaitAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Determine whether the idempotency monitor should keep running.
    /// </summary>
    /// <returns><c>true</c> if the monitor should keep running, otherwise <c>false</c></returns>
    bool MonitorShouldKeepRunning();
}

internal sealed class IdempotencyMonitorExecutionEndArbiter : IIdempotencyMonitorExecutionEndArbiter
{
    private const string LogLabel = "Idempotency Monitor";
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly CancellationTokenSource _interruptCts = new();
    private readonly Lock _lock = new();
    private readonly ILogger<IdempotencyMonitorExecutionEndArbiter> _logger;
    private readonly AsyncManualResetEvent _relevantJobsToObserveEvent = new();
    private readonly ISleepService _sleepService;
    private bool _disposed;

    private int _idempotencyBlockedJobs;
    private bool _relevantJobsToObserveEventIsActive;
    private int _watchedJobsCount;

    private void OnWatchedJobsCountChange(int watchedJobCount)
    {
        bool shouldInterrupt;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _watchedJobsCount = watchedJobCount;
            shouldInterrupt = !ShouldKeepRunningUnsafe();
            ConsiderUpdatingEventUnsafe();
        }

        if (shouldInterrupt)
        {
            TryCancelInterrupt();
        }
    }

    private void OnIdempotencyBlockedJobsCountChange(int idempotencyBlockedJobsCount)
    {
        bool shouldInterrupt;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _idempotencyBlockedJobs = idempotencyBlockedJobsCount;
            shouldInterrupt = !ShouldKeepRunningUnsafe();
            ConsiderUpdatingEventUnsafe();
        }

        if (shouldInterrupt)
        {
            TryCancelInterrupt();
        }
    }

    private void ConsiderUpdatingEventUnsafe()
    {
        if (_idempotencyBlockedJobs == 0)
        {
            // Set to zero from non-zero
            _relevantJobsToObserveEvent.Reset();
            _relevantJobsToObserveEventIsActive = false;
        }
        else if (!_relevantJobsToObserveEventIsActive)
        {
            // Set to non-zero, and was not previously active (suggesting from zero)
            _relevantJobsToObserveEvent.Set();
            _relevantJobsToObserveEventIsActive = true;
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
               || _watchedJobsCount > 0;
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

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (disposing)
        {
            _interruptCts.Dispose();
        }
    }

    public IdempotencyMonitorExecutionEndArbiter(IJobRepository jobRepository, IExecutionEndArbiter executionEndArbiter,
        ISleepService sleepService, ILogger<IdempotencyMonitorExecutionEndArbiter> logger)
    {
        _executionEndArbiter = executionEndArbiter;
        _logger = logger;
        _sleepService = sleepService;
        jobRepository.SubscribeToWatchedJobsUpdate(OnWatchedJobsCountChange);
        jobRepository.SubscribeToIdempotencyBlockedCountUpdate(OnIdempotencyBlockedJobsCountChange);
    }

    public bool MonitorShouldKeepRunning()
    {
        lock (_lock)
        {
            return ShouldKeepRunningUnsafe();
        }
    }

    public async Task IdempotencyMonitorDelayWaitAsync(TimeSpan delay, CancellationToken cancellationToken = default)
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
            // Immediately check to see if the event is set.
            if (!await _relevantJobsToObserveEvent.WaitAsync(TimeSpan.Zero, linkedCts.Token))
            {
                // Event is not set, so wait until it is
                // This wait prevents the monitor from creating noise (trace-level though it may be)
                // when there are no watched jobs to maintain.
                _logger.LogTrace("{LogLabel}: Waiting for watchable events", LogLabel);
                await _relevantJobsToObserveEvent.WaitAsync(linkedCts.Token);
                return;
            }

            _logger.LogTrace("{LogLabel}: {Time} until next follow-up check", LogLabel, delay);
            await _sleepService.DelayAsync(delay, linkedCts.Token);
        }
        catch (OperationCanceledException) when (interruptToken.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            // Interrupt-driven cancellation: treat the delay as having elapsed.
        }
    }

    public void Dispose()
    {
        Dispose(true);
        // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
        GC.SuppressFinalize(this);
    }
}