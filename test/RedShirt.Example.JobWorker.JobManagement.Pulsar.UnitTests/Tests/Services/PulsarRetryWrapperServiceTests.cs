using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

public class PulsarRetryWrapperServiceTests
{
    private static PulsarExceptionArbiterReport TransientReport()
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static PulsarExceptionArbiterReport NonTransientReport()
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static PulsarExceptionArbiterReport AlreadyHandledReport(bool couldBeTransient)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = true,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = false
        };
    }

    private static PulsarExceptionArbiterReport CriticalTransientReport()
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = true,
            CouldBeExternallySolvable = false
        };
    }

    private static PulsarExceptionArbiterReport UnexpectedReport()
    {
        return new PulsarExceptionArbiterReport
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

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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
        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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
    public async Task RunAsync_NonGeneric_WhenOperationCanceledAndTokenCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        sleepService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenTransientFailuresExhaustRetries_WrapsAsHandled()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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
    public async Task RunAsync_NonGeneric_WhenUnexpected_RethrowsRawExceptionWithoutWrapping()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("unexpected");

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(UnexpectedReport());

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => wrapper.RunAsync(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown);
    }

    [Fact]
    public async Task RunAsync_PassesCancellationTokenToFuncAndRetryDelay()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var funcTokens = new List<CancellationToken>();
        var delayTokens = new List<CancellationToken>();
        var attempts = 0;

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(capturedTokens: delayTokens);
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        var result = await wrapper.RunAsync(
            token =>
            {
                funcTokens.Add(token);
                attempts++;
                if (attempts == 1)
                {
                    throw new TimeoutException("retry once");
                }

                return Task.FromResult(true);
            },
            cts.Token);

        Assert.True(result);
        Assert.All(funcTokens, token => Assert.Equal(cts.Token, token));
        Assert.Single(delayTokens);
        Assert.Equal(cts.Token, delayTokens[0]);
    }

    [Fact]
    public async Task RunAsync_ReusesPipelineAcrossInvocations()
    {
        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        Assert.Equal(1, await wrapper.RunAsync(_ => Task.FromResult(1), TestContext.Current.CancellationToken));
        Assert.Equal(2, await wrapper.RunAsync(_ => Task.FromResult(2), TestContext.Current.CancellationToken));
        Assert.Equal(3, await wrapper.RunAsync(_ => Task.FromResult(3), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyHandledCritical_RethrowsSameInstanceWithoutRetry()
    {
        var inner = new WorkerJobSourceException("critical handled")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = false};

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(new PulsarExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        });

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var attempts = 0;
        var inner = new WorkerJobSourceException("already wrapped")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = false};

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(CriticalTransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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
        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(NonTransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync<int>(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        sleepService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenTaskCanceledWithoutCallerCancel_WrapsAsHandledTransient()
    {
        var inner = new TaskCanceledException("broker-style timeout");

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(TransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<string>(
            _ => throw inner,
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.CouldBeTransient);
        Assert.True(thrown.IsHandled);
        Assert.True(thrown.CouldBeExternallySolvable);
    }

    [Fact]
    public async Task RunAsync_WhenTokenCancelledBeforeRetryDecision_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        var inner = new TimeoutException("failed while cancelling");

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
            sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                cts.Cancel();
                throw inner;
            },
            cts.Token));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.IsHandled);
        Assert.True(thrown.CouldBeExternallySolvable);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenTransientFailuresThenSuccess_RetriesWithExponentialDelays()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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

        var arbiter = new Mock<IPulsarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(UnexpectedReport());

        var sleepService = CreateSleepService();
        var wrapper = new PulsarRetryWrapperService(arbiter.Object, NullLogger<PulsarRetryWrapperService>.Instance,
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