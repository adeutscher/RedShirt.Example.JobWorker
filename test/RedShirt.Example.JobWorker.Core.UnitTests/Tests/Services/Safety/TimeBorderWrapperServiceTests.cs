using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Safety;

/// <summary>
///     Unit coverage for <see cref="TimeBorderWrapperService" /> composite-token behaviour.
///     Timed waits are simulated via a mocked <see cref="ISleepService" />; callbacks use
///     <see cref="TaskCompletionSource{TResult}" /> so tests do not rely on wall-clock delays.
/// </summary>
public class TimeBorderWrapperServiceTests
{
    /// <summary>
    ///     First <see cref="ISleepService.WaitAsync{TResult}" /> call times out (job still running);
    ///     later calls invoke <paramref name="onMonitoringWait" /> then return the task (typically after
    ///     completing a <see cref="TaskCompletionSource{TResult}" />).
    /// </summary>
    private static Mock<ISleepService> CreateInitialTimeoutThenCompleteSleepService(
        Action onMonitoringWait)
    {
        var waitCall = 0;
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.WaitAsync(
                It.IsAny<Task<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns((Task<string> task, TimeSpan _, CancellationToken _) =>
            {
                var n = Interlocked.Increment(ref waitCall);
                if (n == 1)
                {
                    Assert.False(task.IsCompleted);
                    return Task.FromException<string>(new TimeoutException());
                }

                onMonitoringWait();
                return task;
            });
        return sleepService;
    }

    private static Mock<ISleepService> CreateCompletedTaskPassthroughSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.WaitAsync(
                It.IsAny<Task<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns((Task<string> task, TimeSpan _, CancellationToken _) => task);
        return sleepService;
    }

    private static void VerifyTruantLogs(Mock<ILogger<TimeBorderWrapperService>> logger)
    {
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("still running", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("completed after exceeding", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    ///     Verifies that <see cref="TimeBorderWrapperService.RunAsync{TIn,TOut}" /> forwards the same input
    ///     instance to the callback under a composite token distinct from the caller token.
    ///     When <paramref name="expectTimeoutCancellation" /> is <see langword="true" />, a cooperative
    ///     callback is cancelled by the composite token; mocked waits time out until the callback faults
    ///     with <see cref="OperationCanceledException" />, without cancelling the caller.
    /// </summary>
    [Theory(Timeout = 5000)]
    [InlineData(null, false)]
    [InlineData(30, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task RunAsync_ForwardsDataUnderCompositeToken_AndInsulatesCallerCancellation(
        int? maximumTimeSeconds,
        bool expectTimeoutCancellation)
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        IJobModel? receivedJob = null;
        CancellationToken observedToken = default;

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        Mock<ISleepService> sleepService;
        if (expectTimeoutCancellation)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            callback
                .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
                .Returns((IJobModel data, CancellationToken token) =>
                {
                    receivedJob = data;
                    observedToken = token;
                    token.Register(() => tcs.TrySetCanceled(token));
                    return tcs.Task;
                });

            // Time out while the cooperative callback is still running; once cancelled, return the task.
            sleepService = new Mock<ISleepService>(MockBehavior.Strict);
            sleepService
                .Setup(s => s.WaitAsync(
                    It.IsAny<Task<string>>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Task<string> task, TimeSpan _, CancellationToken _) =>
                    task.IsCompleted
                        ? task
                        : Task.FromException<string>(new TimeoutException()));
        }
        else
        {
            callback
                .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
                .Returns((IJobModel data, CancellationToken token) =>
                {
                    receivedJob = data;
                    observedToken = token;
                    return Task.FromResult("ok");
                });
            sleepService = CreateCompletedTaskPassthroughSleepService();
        }

        // Tiny max time so the composite CTS cancels without a meaningful wall-clock wait.
        TimeSpan? maximumTime = null;
        if (maximumTimeSeconds is { } seconds)
        {
            maximumTime = expectTimeoutCancellation
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromSeconds(seconds);
        }

        var service = new TimeBorderWrapperService(
            sleepService.Object,
            Options.Create(new TimeBorderWrapperService.ConfigurationModel
            {
                TaskWaitBufferSeconds = null,
                TruantAlertIntervalSeconds = 1
            }),
            NullLogger<TimeBorderWrapperService>.Instance);

        using var callerCts = new CancellationTokenSource();
        var originalCancellationToken = callerCts.Token;

        if (expectTimeoutCancellation)
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.RunAsync(job.Object, maximumTime, callback.Object, originalCancellationToken));

            Assert.NotNull(exception);
        }
        else
        {
            var result = await service.RunAsync(job.Object, maximumTime, callback.Object, originalCancellationToken);

            Assert.Equal("ok", result);
            Assert.False(observedToken.IsCancellationRequested);
        }

        Assert.Same(job.Object, receivedJob);
        callback.Verify(c => c(job.Object, observedToken), Times.Once);
        Assert.NotEqual(originalCancellationToken, observedToken);
        Assert.False(originalCancellationToken.IsCancellationRequested);
        Assert.False(callerCts.IsCancellationRequested);
    }

    /// <summary>
    ///     After the initial time border expires, truant monitoring must not swallow a
    ///     <see cref="TimeoutException" /> thrown by the callback itself.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task RunAsync_WhenTruantCallbackThrowsTimeoutException_PropagatesCallbackException()
    {
        var expected = new TimeoutException("timeout-from-callback");
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel _, CancellationToken _) => tcs.Task);

        var sleepService = CreateInitialTimeoutThenCompleteSleepService(() => tcs.TrySetException(expected));

        var service = new TimeBorderWrapperService(
            sleepService.Object,
            Options.Create(new TimeBorderWrapperService.ConfigurationModel
            {
                TaskWaitBufferSeconds = null,
                TruantAlertIntervalSeconds = 1
            }),
            NullLogger<TimeBorderWrapperService>.Instance);

        using var callerCts = new CancellationTokenSource();
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            service.RunAsync(
                job.Object,
                TimeSpan.FromSeconds(1),
                callback.Object,
                callerCts.Token));

        Assert.Same(expected, exception);
        Assert.False(callerCts.IsCancellationRequested);
        callback.Verify(c => c(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    ///     Same truant-monitoring path as
    ///     <see cref="RunAsync_WhenTruantJobOutlastsMaxTimePlusBuffer_ReturnsCallbackResultViaMonitoring" />,
    ///     but with a null or non-positive <see cref="TimeBorderWrapperService.ConfigurationModel.TaskWaitBufferSeconds" />
    ///     so the wait limit falls back to <see cref="TimeBorderWrapperService.DefaultTaskWaitBufferSeconds" />.
    /// </summary>
    [Theory(Timeout = 5000)]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RunAsync_WhenTruantJobOutlastsDefaultBufferFallback_ReturnsCallbackResultViaMonitoring(
        int? taskWaitBufferSeconds)
    {
        const int maxTimeSeconds = 1;
        var configuration = new TimeBorderWrapperService.ConfigurationModel
        {
            TaskWaitBufferSeconds = taskWaitBufferSeconds,
            TruantAlertIntervalSeconds = 1
        };

        Assert.Equal(
            TimeBorderWrapperService.DefaultTaskWaitBufferSeconds,
            configuration.EffectiveTaskWaitBufferSeconds);

        var expectedWaitLimit = TimeSpan.FromSeconds(
            maxTimeSeconds + configuration.EffectiveTaskWaitBufferSeconds);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        IJobModel? receivedJob = null;
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel data, CancellationToken _) =>
            {
                receivedJob = data;
                return tcs.Task;
            });

        var sleepService = CreateInitialTimeoutThenCompleteSleepService(() => tcs.TrySetResult("truant-finished"));
        var logger = new Mock<ILogger<TimeBorderWrapperService>>();
        var service = new TimeBorderWrapperService(
            sleepService.Object,
            Options.Create(configuration),
            logger.Object);

        using var callerCts = new CancellationTokenSource();
        var result = await service.RunAsync(
            job.Object,
            TimeSpan.FromSeconds(maxTimeSeconds),
            callback.Object,
            callerCts.Token);

        Assert.Equal("truant-finished", result);
        Assert.Same(job.Object, receivedJob);
        Assert.False(callerCts.IsCancellationRequested);
        callback.Verify(c => c(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        sleepService.Verify(
            s => s.WaitAsync(
                It.IsAny<Task<string>>(),
                expectedWaitLimit,
                // ReSharper disable once AccessToDisposedClosure
                callerCts.Token),
            Times.Once);

        VerifyTruantLogs(logger);
    }

    /// <summary>
    ///     A non-cooperative callback that outlasts <c>maximumTime</c> plus
    ///     <see cref="TimeBorderWrapperService.ConfigurationModel.EffectiveTaskWaitBufferSeconds" />
    ///     enters truant monitoring and still returns its result when it eventually completes.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task RunAsync_WhenTruantJobOutlastsMaxTimePlusBuffer_ReturnsCallbackResultViaMonitoring()
    {
        const int maxTimeSeconds = 1;
        var configuration = new TimeBorderWrapperService.ConfigurationModel
        {
            TaskWaitBufferSeconds = 1,
            TruantAlertIntervalSeconds = 1
        };
        var expectedWaitLimit = TimeSpan.FromSeconds(
            maxTimeSeconds + configuration.EffectiveTaskWaitBufferSeconds);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        IJobModel? receivedJob = null;
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel data, CancellationToken _) =>
            {
                receivedJob = data;
                return tcs.Task;
            });

        var sleepService = CreateInitialTimeoutThenCompleteSleepService(() => tcs.TrySetResult("truant-finished"));
        var logger = new Mock<ILogger<TimeBorderWrapperService>>();
        var service = new TimeBorderWrapperService(
            sleepService.Object,
            Options.Create(configuration),
            logger.Object);

        using var callerCts = new CancellationTokenSource();
        var result = await service.RunAsync(
            job.Object,
            TimeSpan.FromSeconds(maxTimeSeconds),
            callback.Object,
            callerCts.Token);

        Assert.Equal("truant-finished", result);
        Assert.Same(job.Object, receivedJob);
        Assert.False(callerCts.IsCancellationRequested);
        callback.Verify(c => c(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        sleepService.Verify(
            s => s.WaitAsync(
                It.IsAny<Task<string>>(),
                expectedWaitLimit,
                // ReSharper disable once AccessToDisposedClosure
                callerCts.Token),
            Times.Once);

        VerifyTruantLogs(logger);
    }
}