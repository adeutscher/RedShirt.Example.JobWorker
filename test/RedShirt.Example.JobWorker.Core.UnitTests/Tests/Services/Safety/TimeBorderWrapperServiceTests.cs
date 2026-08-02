using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Safety;

/// <summary>
///     Unit coverage for <see cref="TimeBorderWrapperService" /> composite-token behaviour.
/// </summary>
public class TimeBorderWrapperServiceTests
{
    /// <summary>
    ///     Verifies that <see cref="TimeBorderWrapperService.RunAsync{TIn,TOut}" /> forwards the same input
    ///     instance to the callback under a composite token distinct from the caller token.
    ///     When <paramref name="expectTimeoutCancellation" /> is <see langword="true" />, a slow cooperative
    ///     callback is cancelled by the composite token; after the initial wait times out, monitoring continues
    ///     until the callback faults with <see cref="OperationCanceledException" />, without cancelling the caller.
    /// </summary>
    [Theory(Timeout = 10000)]
    [InlineData(null, false)]
    [InlineData(30, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task RunAsync_ForwardsDataUnderCompositeToken_AndInsulatesCallerCancellation(
        int? maximumTimeSeconds,
        bool expectTimeoutCancellation)
    {
        // Input data
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        IJobModel? receivedJob = null;
        CancellationToken observedToken = default;

        // Callback under test (Moq)
        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel data, CancellationToken token) =>
            {
                receivedJob = data;
                observedToken = token;

                if (expectTimeoutCancellation)
                {
                    await Task.Delay(TimeSpan.FromSeconds(maximumTimeSeconds!.Value + 2), token);
                }

                return "ok";
            });

        // System under test
        var service = new TimeBorderWrapperService(
            Options.Create(new TimeBorderWrapperService.ConfigurationModel
            {
                TaskWaitBufferSeconds = null,
                TruantAlertIntervalSeconds = 1
            }),
            NullLogger<TimeBorderWrapperService>.Instance);
        var maximumTime = maximumTimeSeconds is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?) null;

        using var callerCts = new CancellationTokenSource();
        var originalCancellationToken = callerCts.Token;

        if (expectTimeoutCancellation)
        {
            // Composite cancel + truant monitor surface the callback's OperationCanceledException.
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

        // Same input instance reaches the callback
        Assert.Same(job.Object, receivedJob);
        callback.Verify(c => c(job.Object, observedToken), Times.Once);

        // Composite token is not the caller's token; caller remains uncancelled
        Assert.NotEqual(originalCancellationToken, observedToken);
        Assert.False(originalCancellationToken.IsCancellationRequested);
        Assert.False(callerCts.IsCancellationRequested);
    }

    /// <summary>
    ///     After the initial time border expires, truant monitoring must not swallow a
    ///     <see cref="TimeoutException" /> thrown by the callback itself.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RunAsync_WhenTruantCallbackThrowsTimeoutException_PropagatesCallbackException()
    {
        var expected = new TimeoutException("timeout-from-callback");
        var job = new Mock<IJobModel>(MockBehavior.Strict);

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken _) =>
            {
                // Ignore the composite token so the job stays running past maxTime and enters truant monitoring.
                await Task.Delay(TimeSpan.FromMilliseconds(1500), CancellationToken.None);
                throw expected;
            });

        var service = new TimeBorderWrapperService(
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
    ///     My apologies to your build pipeline, but it's for a good cause.
    /// </summary>
    [Theory(Timeout = 30000)]
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

        // Outlast cooperative cancel (maxTime) and the effective (default) WaitAsync buffer.
        var truantDuration = TimeSpan.FromSeconds(
            maxTimeSeconds + configuration.EffectiveTaskWaitBufferSeconds + 2);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        IJobModel? receivedJob = null;

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel data, CancellationToken _) =>
            {
                receivedJob = data;
                await Task.Delay(truantDuration, CancellationToken.None);
                return "truant-finished";
            });

        var logger = new Mock<ILogger<TimeBorderWrapperService>>();
        var service = new TimeBorderWrapperService(
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
    ///     A non-cooperative callback that outlasts <c>maximumTime</c> plus
    ///     <see cref="TimeBorderWrapperService.ConfigurationModel.EffectiveTaskWaitBufferSeconds" />
    ///     enters truant monitoring and still returns its result when it eventually completes.
    ///     <para>
    ///         Acknowledgement: this is intentionally a long-running test (~maxTime + buffer + 2 seconds of real
    ///         wall-clock delay) so the initial <c>WaitAsync</c> expires and truant monitoring is exercised.
    ///         My apologies to your build pipeline, but it's for a good cause.
    ///     </para>
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhenTruantJobOutlastsMaxTimePlusBuffer_ReturnsCallbackResultViaMonitoring()
    {
        const int maxTimeSeconds = 1;
        var configuration = new TimeBorderWrapperService.ConfigurationModel
        {
            // Small buffer keeps the test shorter while still exercising monitoring.
            TaskWaitBufferSeconds = 1,
            TruantAlertIntervalSeconds = 1
        };
        // Outlast cooperative cancel (maxTime) and the WaitAsync buffer so monitoring is entered.
        var truantDuration = TimeSpan.FromSeconds(
            maxTimeSeconds + configuration.EffectiveTaskWaitBufferSeconds + 2);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        IJobModel? receivedJob = null;

        var callback = new Mock<Func<IJobModel, CancellationToken, Task<string>>>(MockBehavior.Strict);
        callback
            .Setup(c => c(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel data, CancellationToken _) =>
            {
                receivedJob = data;
                // Ignore the composite token — a true truant that does not honour cancellation.
                await Task.Delay(truantDuration, CancellationToken.None);
                return "truant-finished";
            });

        var logger = new Mock<ILogger<TimeBorderWrapperService>>();
        var service = new TimeBorderWrapperService(
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
}