using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.Models;
using RedShirt.Example.JobWorker.Common.Azure.Services;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Services;

public class AzureRetryWrapperServiceTests
{
    private static AzureExceptionArbiterReport TransientReport()
    {
        return new AzureExceptionArbiterReport
        {
            IsExpected = true,
            CouldBeTransient = true
        };
    }

    private static AzureExceptionArbiterReport NonTransientReport()
    {
        return new AzureExceptionArbiterReport
        {
            IsExpected = true,
            CouldBeTransient = false
        };
    }

    private static AzureExceptionArbiterReport UnexpectedReport(bool couldBeTransient = false)
    {
        return new AzureExceptionArbiterReport
        {
            IsExpected = false,
            CouldBeTransient = couldBeTransient
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
    public async Task RunAsync_PassesCancellationTokenToFuncAndRetryDelay()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var funcTokens = new List<CancellationToken>();
        var delayTokens = new List<CancellationToken>();
        var attempts = 0;

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(capturedTokens: delayTokens);
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var result = await wrapper.RunAsync(
            token =>
            {
                funcTokens.Add(token);
                attempts++;
                if (attempts == 1)
                {
                    throw new HttpRequestException("retry once");
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
        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        Assert.Equal(1, await wrapper.RunAsync(_ => Task.FromResult(1), TestContext.Current.CancellationToken));
        Assert.Equal(2, await wrapper.RunAsync(_ => Task.FromResult(2), TestContext.Current.CancellationToken));
        Assert.Equal(3, await wrapper.RunAsync(_ => Task.FromResult(3), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResultWithoutSleeping()
    {
        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

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

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(inner)).Returns(NonTransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerAzureException>(() => wrapper.RunAsync<int>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown.InnerException);
        Assert.False(thrown.IsTransient);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenOperationCanceledAndTokenCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync<int>(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        sleepService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenSleepCancelledDuringRetry_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((_, token) =>
            {
                cts.Cancel();
                return Task.FromCanceled(token);
            });

        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                throw new HttpRequestException("transient");
            },
            cts.Token));

        Assert.Equal(1, attempts);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(1), cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenTaskCanceledWithoutCallerCancel_WrapsUsingArbiterJudgement()
    {
        var inner = new TaskCanceledException("http-style timeout");
        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(inner)).Returns(TransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerAzureException>(() => wrapper.RunAsync<string>(
            _ => throw inner,
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.IsTransient);
    }

    [Fact]
    public async Task RunAsync_WhenTokenCancelledBeforeRetryDecision_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        var inner = new HttpRequestException("failed while cancelling");

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        // If judgement is consulted after cancel, still report transient; cancel should win in the judge.
        arbiter.Setup(a => a.GetJudgement(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerAzureException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                cts.Cancel();
                throw inner;
            },
            cts.Token));

        Assert.Equal(1, attempts);
        Assert.Same(inner, thrown.InnerException);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenTransientFailuresExhaustRetries_ThrowsWrapperWithTransientFlag()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new HttpRequestException("still failing");

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerAzureException>(() => wrapper.RunAsync<string>(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.IsTransient);
        // original attempt + 3 retries
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
    public async Task RunAsync_WhenTransientFailuresThenSuccess_RetriesWithExponentialDelays()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var result = await wrapper.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new HttpRequestException($"transient failure #{attempts}");
                }

                return Task.FromResult("recovered");
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("recovered", result);
        Assert.Equal(3, attempts);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            delays);
        arbiter.Verify(a => a.GetJudgement(It.IsAny<Exception>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenUnexpected_RethrowsRawExceptionWithoutWrapping(bool couldBeTransient)
    {
        var attempts = 0;
        var inner = new NotSupportedException("unexpected");

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetJudgement(inner)).Returns(UnexpectedReport(couldBeTransient));

        var sleepService = CreateSleepService();
        var wrapper = new AzureRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<NotSupportedException>(() => wrapper.RunAsync<int>(
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