using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisDistributedRetryWrapperServiceTests
{
    private static RedisExceptionArbiterReport TransientReport()
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = false,
            CouldBeTransient = true
        };
    }

    private static RedisExceptionArbiterReport NonTransientReport()
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = false,
            CouldBeTransient = false
        };
    }

    private static RedisExceptionArbiterReport AlreadyHandledReport(bool couldBeTransient)
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = true,
            CouldBeTransient = couldBeTransient
        };
    }

    private static Mock<IDistributedSleepService> CreateSleepService(
        IList<TimeSpan>? capturedDelays = null,
        IList<CancellationToken>? capturedTokens = null)
    {
        var sleepService = new Mock<IDistributedSleepService>(MockBehavior.Strict);
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
        var inner = new WorkerDistributedException("already wrapped");

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync(
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
        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);
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

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        sleepService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenTransientFailuresExhaustRetries_Wraps()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync(
            _ =>
            {
                attempts++;
                throw inner;
            },
            TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown.InnerException);
        Assert.True(thrown.IsTransient);
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
    public async Task RunAsync_PassesCancellationTokenToFuncAndRetryDelay()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var funcTokens = new List<CancellationToken>();
        var delayTokens = new List<CancellationToken>();
        var attempts = 0;

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(capturedTokens: delayTokens);
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

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
        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        Assert.Equal(1, await wrapper.RunAsync(_ => Task.FromResult(1), TestContext.Current.CancellationToken));
        Assert.Equal(2, await wrapper.RunAsync(_ => Task.FromResult(2), TestContext.Current.CancellationToken));
        Assert.Equal(3, await wrapper.RunAsync(_ => Task.FromResult(3), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var attempts = 0;
        var inner = new WorkerDistributedException("already wrapped");

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport(false));

        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync<int>(
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
        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

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

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(NonTransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync<int>(
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

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.RunAsync<int>(
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));

        arbiter.VerifyNoOtherCalls();
        sleepService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenTaskCanceledWithoutCallerCancel_WrapsAsTransient()
    {
        var inner = new TaskCanceledException("http-style timeout");

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(TransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync<string>(
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
        var inner = new TimeoutException("failed while cancelling");

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService();
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync<string>(
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
    public async Task RunAsync_WhenTransientFailuresExhaustRetries_ThrowsWorkerDistributedException()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new RedisTimeoutException("still failing", CommandStatus.Unknown);

        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() => wrapper.RunAsync<string>(
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
        var arbiter = new Mock<IRedisDistributedExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleepService = CreateSleepService(delays);
        var wrapper = new RedisDistributedRetryWrapperService(arbiter.Object, sleepService.Object);

        var result = await wrapper.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new RedisConnectionException(ConnectionFailureType.UnableToConnect,
                        $"transient failure #{attempts}");
                }

                return Task.FromResult("recovered");
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("recovered", result);
        Assert.Equal(3, attempts);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            delays);
        // ShouldHandle consults the arbiter once per failed attempt.
        arbiter.Verify(a => a.GetReport(It.IsAny<Exception>()), Times.Exactly(2));
    }
}