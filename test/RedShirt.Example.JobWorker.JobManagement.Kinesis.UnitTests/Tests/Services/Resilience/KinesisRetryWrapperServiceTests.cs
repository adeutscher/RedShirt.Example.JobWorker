using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services.Resilience;

public class KinesisRetryWrapperServiceTests
{
    private static KinesisExceptionArbiterReport TransientReport()
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static KinesisExceptionArbiterReport PermanentReport()
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static KinesisExceptionArbiterReport CriticalReport()
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static KinesisExceptionArbiterReport AlreadyHandledReport(bool couldBeTransient)
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = true,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = false
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
    public async Task RunAsync_NonGeneric_WhenFuncSucceeds_CompletesWithoutSleeping()
    {
        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);
        var ran = false;

        await wrapper.RunAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.True(ran);
        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var inner = new WorkerJobSourceException("already wrapped")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = false};

        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleep = CreateSleepService();
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            wrapper.RunAsync(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenCriticalFailure_ThrowsRaw()
    {
        var inner = new InvalidOperationException("critical");

        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(CriticalReport());

        var sleep = CreateSleepService();
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrapper.RunAsync<string>(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResultWithoutSleeping()
    {
        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        var result = await wrapper.RunAsync(_ => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        arbiter.VerifyNoOtherCalls();
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenOperationCanceledAndTokenCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentFailure_WrapsWithoutRetry()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("permanent");

        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(PermanentReport());

        var sleep = CreateSleepService();
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.IsHandled);
        Assert.False(thrown.CouldBeTransient);
        Assert.False(thrown.CouldBeExternallySolvable);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenTransientFailuresExhaustRetries_WrapsAsWorkerJobSourceException()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.CouldBeTransient);
        Assert.True(thrown.IsHandled);
        Assert.True(thrown.CouldBeExternallySolvable);
        Assert.Equal(4, attempts);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)],
            delays);
    }

    [Fact]
    public async Task RunAsync_WhenTransientThenSucceeds_RetriesWithBackoff()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var arbiter = new Mock<IKinesisExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper = new KinesisRetryWrapperService(arbiter.Object, NullLogger<KinesisRetryWrapperService>.Instance,
            sleep.Object);

        var result = await wrapper.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new TimeoutException("retry once");
                }

                return Task.FromResult("ok");
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }
}