using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Models;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.Services;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.UnitTests.Tests.Services.Resilience;

public class SsmRetryWrapperServiceTests
{
    private static SsmExceptionArbiterReport TransientReport()
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static SsmExceptionArbiterReport PermanentReport()
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static SsmExceptionArbiterReport CriticalReport()
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static SsmExceptionArbiterReport AlreadyHandledReport()
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
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
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var inner = new WorkerSecretManagerException("already wrapped")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = true};

        var arbiter = new Mock<ISsmExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport());

        var sleep = CreateSleepService();
        var wrapper =
            new SsmRetryWrapperService(arbiter.Object, NullLogger<SsmRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
            wrapper.RunAsync<string>(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        Assert.Equal(inner.CouldBeExternallySolvable, thrown.CouldBeExternallySolvable);
    }

    [Fact]
    public async Task RunAsync_WhenCriticalFailure_ThrowsRaw()
    {
        var inner = new InvalidOperationException("critical");

        var arbiter = new Mock<ISsmExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(CriticalReport());

        var sleep = CreateSleepService();
        var wrapper =
            new SsmRetryWrapperService(arbiter.Object, NullLogger<SsmRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrapper.RunAsync<string>(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResultWithoutSleeping()
    {
        var arbiter = new Mock<ISsmExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper =
            new SsmRetryWrapperService(arbiter.Object, NullLogger<SsmRetryWrapperService>.Instance, sleep.Object);

        var result = await wrapper.RunAsync(_ => Task.FromResult("secret"), TestContext.Current.CancellationToken);

        Assert.Equal("secret", result);
        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenPermanentFailure_WrapsWithoutRetry()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("permanent");

        var arbiter = new Mock<ISsmExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(PermanentReport());

        var sleep = CreateSleepService();
        var wrapper =
            new SsmRetryWrapperService(arbiter.Object, NullLogger<SsmRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() => wrapper.RunAsync<string>(
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
    public async Task RunAsync_WhenTransientFailuresExhaustRetries_WrapsAsWorkerSecretManagerException()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<ISsmExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper =
            new SsmRetryWrapperService(arbiter.Object, NullLogger<SsmRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() => wrapper.RunAsync<string>(
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

        var arbiter = new Mock<ISsmExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper =
            new SsmRetryWrapperService(arbiter.Object, NullLogger<SsmRetryWrapperService>.Instance, sleep.Object);

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