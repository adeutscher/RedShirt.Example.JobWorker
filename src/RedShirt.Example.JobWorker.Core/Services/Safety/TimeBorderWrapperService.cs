using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.Safety;

/// <summary>
///     Runs a callback under a composite cancellation token so that a per-invocation time limit
///     (or job-local cancellation) does not cancel the caller's token.
/// </summary>
internal interface ITimeBorderWrapperService
{
    /// <summary>
    ///     Invoke <paramref name="callback" /> with a composite <see cref="CancellationToken" />.
    ///     When <paramref name="maximumTime" /> is <see langword="null" />, the composite token sources from
    ///     <paramref name="cancellationToken" /> and <see cref="CancellationToken.None" />.
    ///     When <paramref name="maximumTime" /> is set, the composite token sources from
    ///     <paramref name="cancellationToken" /> and a token that cancels after that duration.
    ///     To confirm phrasing, this wrapper execution shall not handle any exceptions.
    ///     Any exceptions thrown, including <see cref="OperationCanceledException" />, should
    ///     be handled by the invoking method.
    /// </summary>
    /// <param name="data">Input forwarded to <paramref name="callback" />.</param>
    /// <param name="maximumTime">Optional maximum duration before the composite token is cancelled.</param>
    /// <param name="callback">Work to run under the composite token.</param>
    /// <param name="cancellationToken">Caller token linked into the composite token.</param>
    /// <remarks>
    ///     When <paramref name="maximumTime" /> is set, the callback task is also awaited via
    ///     <see cref="ISleepService.WaitAsync{TResult}" /> for
    ///     <paramref name="maximumTime" /> plus
    ///     <see cref="TimeBorderWrapperService.ConfigurationModel.EffectiveTaskWaitBufferSeconds" />,
    ///     which throws <see cref="TimeoutException" /> if that wait expires before the callback completes
    ///     (or <see cref="OperationCanceledException" /> if <paramref name="cancellationToken" /> is cancelled first).
    ///     A cooperative callback that observes the composite token may still fault with
    ///     <see cref="OperationCanceledException" /> before the wait times out.
    ///     When <c>WaitAsync</c> times out while the callback is still running, monitoring continues via
    ///     periodic waits at <see cref="TimeBorderWrapperService.ConfigurationModel.EffectiveTruantAlertInterval" />
    ///     with warning logs until the callback completes; the callback result (or fault) is then returned.
    ///     A <see cref="TimeoutException" /> from the callback itself is not swallowed by truant monitoring and
    ///     propagates to the caller. A <see cref="TimeoutException" /> from the initial wait when the task already
    ///     completed (fault or cancellation race) is surfaced by awaiting the callback task.
    /// </remarks>
    Task<TOut> RunAsync<TIn, TOut>(
        TIn data,
        TimeSpan? maximumTime,
        Func<TIn, CancellationToken, Task<TOut>> callback,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Default <see cref="ITimeBorderWrapperService" /> using linked <see cref="CancellationTokenSource" /> instances.
/// </summary>
/// <param name="sleepService">
///     Timed wait abstraction wrapping
///     <see cref="Task.WaitAsync(System.TimeSpan, System.Threading.CancellationToken)" />.
/// </param>
/// <param name="options">Truant-alert configuration.</param>
/// <param name="logger">Logger for truant-callback alerts.</param>
internal sealed class TimeBorderWrapperService(
    ISleepService sleepService,
    IOptions<TimeBorderWrapperService.ConfigurationModel> options,
    ILogger<TimeBorderWrapperService> logger) : ITimeBorderWrapperService
{
    /// <summary>
    ///     Default extra seconds added to <c>maximumTime</c> for the initial <see cref="ISleepService.WaitAsync{TResult}" />
    ///     when <see cref="ConfigurationModel.TaskWaitBufferSeconds" /> is null or non-positive.
    /// </summary>
    internal const int DefaultTaskWaitBufferSeconds = 5;

    /// <summary>
    ///     Periodically <see cref="ISleepService.WaitAsync{TResult}" /> the
    ///     still-running callback and log until it completes, then return its result.
    ///     Refer to the comment block above this method's invocation for more detail
    ///     on why the decision was made to monitor an ongoing callback.
    /// </summary>
    private async Task<TOut> MonitorTruantCallbackAsync<TOut>(
        TimeSpan maxTime,
        Task<TOut> jobTask,
        CancellationToken cancellationToken)
    {
        var alertInterval = options.Value.EffectiveTruantAlertInterval;
        var buffer = TimeSpan.FromSeconds(options.Value.EffectiveTaskWaitBufferSeconds);
        var alertCount = 0;

        while (true)
        {
            alertCount++;
            logger.LogError(
                "Truant job callback still running after max time {MaxTime} plus buffer {Buffer}; alert {AlertCount}, next check in {Interval}",
                maxTime, buffer, alertCount, alertInterval);

            try
            {
                var result = await sleepService.WaitAsync(jobTask, alertInterval, cancellationToken);
                logger.LogWarning(
                    "Truant job callback completed after exceeding max time {MaxTime} plus buffer {Buffer}. Alerts raised: {AlertCount}",
                    maxTime, buffer, alertCount);
                return result;
            }
            catch (TimeoutException) when (!jobTask.IsCompleted)
            {
                // Still running — loop and alert again before the next wait.
            }
            catch (TimeoutException) when (jobTask.IsCompleted)
            {
                // Completed in a race with WaitAsync's timeout — surface that outcome.
                var result = await jobTask;
#pragma warning disable S6667
                // Acknowledging Sonar's preference to put the exception in the LogWarning call,
                //  but I think that it would just be unhelpful noise. 
                logger.LogWarning(
                    "Truant job callback completed after exceeding max time {MaxTime} plus buffer {Buffer}. Alerts raised: {AlertCount}",
                    maxTime, buffer, alertCount);
#pragma warning restore S6667
                return result;
            }
        }
    }

    public async Task<TOut> RunAsync<TIn, TOut>(
        TIn data,
        TimeSpan? maximumTime,
        Func<TIn, CancellationToken, Task<TOut>> callback,
        CancellationToken cancellationToken = default)
    {
        if (maximumTime is not { } maxTime)
        {
            using var defaultCompositeCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken.None);
            return await callback(data, defaultCompositeCts.Token);
        }

        using var timeoutCts = new CancellationTokenSource(maxTime);
        using var constrainedCompositeCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Cooperative signal via composite token (cancels at maxTime).
        // WaitAsync uses maxTime plus a buffer so that a cooperative callback has time to observe cancel
        // before we treat the job as truant.
        var jobTask = callback(data, constrainedCompositeCts.Token);
        var waitLimit = maxTime + TimeSpan.FromSeconds(options.Value.EffectiveTaskWaitBufferSeconds);
        try
        {
            return await sleepService.WaitAsync(jobTask, waitLimit, cancellationToken);
        }
        catch (TimeoutException) when (!jobTask.IsCompleted)
        {
            /*
             * The above WaitAsync call has expired and the job is not complete.
             *
             * Per Google: The historical Thread.Abort() method is obsolete, unsafe,
             *  and throws a PlatformNotSupportedException in modern versions like
             * .NET Core and .NET 5+. Forcing a thread to kill itself can leave files open,
             * lock resources indefinitely, or corrupt your application data.
             *
             * So, documentation says that we cannot stop a truant callback outright
             *  and that the appropriate strategy is to build logic that respects the use of CancellationToken
             *  using calls such as the CancellationToken.ThrowIfCancellationRequested() method.
             *
             * Technically, we could throw the exception up and let the system pull another job.
             * However, that would not address the root cause of the issue:
             *  The programmed job logic handler is still running for too long
             *  and not respecting the CancellationToken.
             *  This would eventually (or sooner rather than later, depending on job source backlog) pile up
             *  and keep taking resources beyond the configured worker thread limits, degrading performance
             *  and probably crashing the entire program.
             *
             * Keeping this in mind, the least damaging choice that I can think of for the moment
             * is to log periodic alerts while continuing to await the result. The hope is that the
             * logged errors will create urgency to reconfigure the job worker with more appropriate
             * limits or reprogram the job logic to respect CancellationToken.
             * Preferably both.
             *
             * A *POSSIBLE* solution to this problem that *MIGHT* allow one to strictly enforce a maximum time
             * *COULD* be to pass the parameters of a payload into an entirely separate process
             * that shall directly process that payload.
             * We cannot terminate a thread, but surely we could outright terminate a rogue process.
             * This is *NOT* a fully baked idea. This is just something that I am pondering as I was implementing
             * this time border service. It would, in theory, allow us to terminate rogue jobs. However, the trade-off
             * to this is that it would at the very least become significantly more awkward to trace output and relay
             * exception information back out through an intermediate service such as this one.
             * Please take this pondering on using a separate process with a grain of salt, as it hasn't been tested in any way.
             */

            return await MonitorTruantCallbackAsync(maxTime, jobTask, cancellationToken);
        }
        catch (TimeoutException) when (jobTask.IsCompleted)
        {
            // Callback finished (faulted or cancelled) in a race with WaitAsync's timeout — surface that outcome.
            return await jobTask;
        }
    }

    public sealed class ConfigurationModel
    {
        private const int DefaultTruantAlertIntervalSeconds = 30;

        /// <summary>
        ///     Extra seconds added to <c>maximumTime</c> for the initial <see cref="ISleepService.WaitAsync{TResult}" />
        ///     so cooperative cancellation can take effect before truant monitoring begins.
        ///     Null or non-positive values fall back to
        ///     <see cref="TimeBorderWrapperService.DefaultTaskWaitBufferSeconds" /> via
        ///     <see cref="EffectiveTaskWaitBufferSeconds" />.
        /// </summary>
        public required int? TaskWaitBufferSeconds { get; init; }

        /// <summary>
        ///     Buffer seconds used for the initial wait:
        ///     <see cref="TaskWaitBufferSeconds" /> when not null and greater than zero; otherwise
        ///     <see cref="TimeBorderWrapperService.DefaultTaskWaitBufferSeconds" />.
        /// </summary>
        public int EffectiveTaskWaitBufferSeconds =>
            TaskWaitBufferSeconds is > 0
                ? TaskWaitBufferSeconds.Value
                : DefaultTaskWaitBufferSeconds;

        /// <summary>
        ///     Seconds between log alerts while a timed-out callback continues to run.
        ///     Non-positive values fall back to the default used by
        ///     <see cref="EffectiveTruantAlertInterval" />.
        /// </summary>
        public required int TruantAlertIntervalSeconds { get; init; }

        /// <summary>
        ///     Alert interval used when monitoring a truant callback:
        ///     <see cref="TruantAlertIntervalSeconds" /> when greater than zero; otherwise 30 seconds.
        /// </summary>
        public TimeSpan EffectiveTruantAlertInterval =>
            TruantAlertIntervalSeconds > 0
                ? TimeSpan.FromSeconds(TruantAlertIntervalSeconds)
                : TimeSpan.FromSeconds(DefaultTruantAlertIntervalSeconds);
    }
}