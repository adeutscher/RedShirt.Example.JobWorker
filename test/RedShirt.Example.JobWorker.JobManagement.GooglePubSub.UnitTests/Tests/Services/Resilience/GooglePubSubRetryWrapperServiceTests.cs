using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;

public class GooglePubSubRetryWrapperServiceTests
{
    private static GooglePubSubExceptionArbiterReport TransientReport()
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static GooglePubSubExceptionArbiterReport NonTransientReport()
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static GooglePubSubExceptionArbiterReport AlreadyHandledReport(bool couldBeTransient)
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = true,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = false
        };
    }

    private static GooglePubSubExceptionArbiterReport CriticalTransientReport()
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = true,
            CouldBeExternallySolvable = false
        };
    }

    private static GooglePubSubExceptionArbiterReport UnexpectedReport()
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static Mock<ISleepService> CreateSleepService(
        IList<TimeSpan>? capturedDelays = null,
        IList<CancellationToken>? capturedTokens = null)
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((delay, cancellationToken) =>
            {
                capturedDelays?.Add(delay);
                capturedTokens?.Add(cancellationToken);
                return Task.CompletedTask;
            });
        return sleepService;
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var inner = new WorkerJobSourceException("already wrapped")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = false};

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync(
            _ => throw inner,
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenFuncSucceeds_CompletesWithoutSleeping()
    {
        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);
        var ran = false;

        await wrapper.RunAsync(
            _ =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.True(ran);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenTransientFailuresExhaustRetries_WrapsAsHandled()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync(
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
            [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4)
            ],
            delays);
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var attempts = 0;
        var inner = new WorkerJobSourceException("already wrapped")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = false};

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<int>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenCriticalEvenIfTransient_DoesNotRetryAndRethrowsRaw()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("critical but looks transient");

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(CriticalTransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => wrapper.RunAsync<int>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResultWithoutSleeping()
    {
        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var result = await wrapper.RunAsync(
            _ => Task.FromResult(42),
            TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenNonTransient_DoesNotRetry()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("not retryable");

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(NonTransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<int>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown.InnerException);
        Assert.False(thrown.CouldBeTransient);
        Assert.True(thrown.IsHandled);
        Assert.False(thrown.CouldBeExternallySolvable);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenOperationCanceledAndTokenCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync<int>(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        sleepService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenTransientFailuresThenSuccess_RetriesWithExponentialDelays()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var result = await wrapper.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new TimeoutException($"transient failure #{attempts}");
                }

                return Task.FromResult("recovered");
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("recovered", result);
        Assert.Equal(3, attempts);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            delays);
        arbiter.Verify(a => a.GetReport(It.IsAny<Exception>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_WhenUnexpected_RethrowsRawExceptionWithoutWrapping()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("unexpected");

        var arbiter = new Mock<IGooglePubSubExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(UnexpectedReport());

        var sleepService = CreateSleepService();
        var wrapper = new GooglePubSubRetryWrapperService(arbiter.Object,
            NullLogger<GooglePubSubRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => wrapper.RunAsync<int>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}