using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services.Utility;

/// <summary>
///     Sleep helpers that are aware of application stop via <see cref="IExecutionEndArbiter" />.
/// </summary>
public interface ICoreSleepService
{
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
