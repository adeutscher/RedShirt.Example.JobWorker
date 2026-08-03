using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services.Utility;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

public class RedisStreamsRetryWrapperServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsResult_OnSuccess()
    {
        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        var sut = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var result = await sut.RunAsync(_ => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        arbiter.VerifyNoOtherCalls();
        sleep.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_RetriesTransientThenSucceeds()
    {
        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<RedisTimeoutException>()))
            .Returns(new RedisStreamsExceptionArbiterReport
            {
                AlreadyHandled = false,
                IsCritical = false,
                CouldBeTransient = true
            });

        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        sleep.Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);
        var attempts = 0;

        var result = await sut.RunAsync(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new RedisTimeoutException("timeout", CommandStatus.Unknown);
            }

            return Task.FromResult("ok");
        }, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        sleep.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WrapsPermanentFailureAsWorkerJobSourceException()
    {
        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<ArgumentException>()))
            .Returns(new RedisStreamsExceptionArbiterReport
            {
                AlreadyHandled = false,
                IsCritical = false,
                CouldBeTransient = false
            });

        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        var sut = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            sut.RunAsync(_ => throw new ArgumentException("bad"), TestContext.Current.CancellationToken));

        Assert.False(thrown.IsCritical);
        Assert.False(thrown.CouldBeTransient);
        Assert.True(thrown.IsHandled);
        sleep.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_PropagatesCriticalExceptionRaw()
    {
        var arbiter = new Mock<IRedisStreamsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<InvalidOperationException>()))
            .Returns(new RedisStreamsExceptionArbiterReport
            {
                AlreadyHandled = false,
                IsCritical = true,
                CouldBeTransient = false
            });

        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        var sut = new RedisStreamsRetryWrapperService(arbiter.Object, sleep.Object);
        var failure = new InvalidOperationException("critical");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunAsync(_ => throw failure, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }
}
