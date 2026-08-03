using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services.Utility;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

public class RedisStreamsRetryWrapperServiceTests
{
    private static RedisStreamsExceptionArbiterReport TransientReport()
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = false,
            CouldBeTransient = true
        };
    }

    private static RedisStreamsExceptionArbiterReport PermanentReport()
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = false,
            CouldBeTransient = false
        };
    }

    private static RedisStreamsExceptionArbiterReport CriticalReport()
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = true,
            CouldBeTransient = false
        };
    }

    private static RedisStreamsExceptionArbiterReport AlreadyHandledReport(bool couldBeTransient)
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = false,
            CouldBeTransient = couldBeTransient
        };
    }

    private static Mock<ISleepService> CreateSleepService(IList<TimeSpan>? capturedDelays = null)
    {
        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        sleep.Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((delay, _) =>
            {
                capturedDelays?.Add(delay);
                return Task.CompletedTask;
            });
        return sleep;
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResultWithoutSleeping()
    {
        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var result = await wrapper.RunAsync(_ => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        arbiter.VerifyNoOtherCalls();
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenFuncSucceeds_CompletesWithoutSleeping()
    {
        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);
        var ran = false;

        await wrapper.RunAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.True(ran);
        arbiter.VerifyNoOtherCalls();
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenTransientThenSucceeds_RetriesWithBackoff()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var result = await wrapper.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new TimeoutException("timeout");
                }

                return Task.FromResult("ok");
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task RunAsync_WhenTransientFailuresExhaustRetries_WrapsAsWorkerJobSourceException()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.False(thrown.IsCritical);
        Assert.True(thrown.CouldBeTransient);
        Assert.True(thrown.IsHandled);
        Assert.Equal(4, attempts);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)],
            delays);
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenTransientFailuresExhaustRetries_Wraps()
    {
        var attempts = 0;
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.IsHandled);
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentFailure_WrapsWithoutRetry()
    {
        var attempts = 0;
        var inner = new ArgumentException("bad");

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(PermanentReport());

        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown.InnerException);
        Assert.False(thrown.IsCritical);
        Assert.False(thrown.CouldBeTransient);
        Assert.True(thrown.IsHandled);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenCriticalFailure_ThrowsRaw()
    {
        var inner = new InvalidOperationException("critical");

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(CriticalReport());

        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrapper.RunAsync<string>(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var inner = new WorkerJobSourceException("already wrapped", false, false, true);

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            wrapper.RunAsync(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenOperationCanceledAndTokenCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenOperationCanceledAndTokenCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync<int>(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        Assert.Empty(sleep.Invocations);
    }
}
