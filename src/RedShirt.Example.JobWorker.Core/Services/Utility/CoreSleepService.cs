using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services.Utility;

/// <summary>
///     Sleep helpers that are aware of application stop via <see cref="IExecutionEndArbiter" />.
///     Also exposes a plain <see cref="DelayAsync" /> pass-through so consumers that need both stop-aware
///     and flat delays can inject a single dependency instead of both
///     <see cref="ICoreSleepService" /> and <see cref="ISleepService" />.
/// </summary>
public interface ICoreSleepService
{
    /// <summary>
    ///     Direct pass-through to <see cref="ISleepService.DelayAsync" />.
    ///     Prefer this over injecting <see cref="ISleepService" /> separately when the consumer already
    ///     depends on <see cref="ICoreSleepService" /> for <see cref="DelayWithStopAwareness" />.
    /// </summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Delays for <paramref name="delay" />, honouring both <paramref name="cancellationToken" /> and
    ///     <see cref="IExecutionEndArbiter.CancellationToken" />.
    ///     Cancellation caused by the arbiter stop token is ignored and treated as a completed delay.
    /// </summary>
    Task DelayWithStopAwareness(TimeSpan delay, CancellationToken cancellationToken = default);
}

internal sealed class CoreSleepService(IExecutionEndArbiter executionEndArbiter, ISleepService sleepService)
    : ICoreSleepService
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return sleepService.DelayAsync(delay, cancellationToken);
    }

    public async Task DelayWithStopAwareness(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, executionEndArbiter.CancellationToken);

        try
        {
            await sleepService.DelayAsync(delay, linkedCts.Token);
        }
        catch (OperationCanceledException) when (executionEndArbiter.CancellationToken.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            // Stop-driven cancellation: treat the delay as having elapsed.
        }
    }
}
