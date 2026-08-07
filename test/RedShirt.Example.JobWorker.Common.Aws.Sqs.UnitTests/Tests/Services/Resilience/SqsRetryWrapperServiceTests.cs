using Amazon.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Models;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Services.Utility;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.UnitTests.Tests.Services.Resilience;

public class SqsRetryWrapperServiceTests
{
    private static SqsExceptionArbiterReport TransientReport()
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static SqsExceptionArbiterReport PermanentReport()
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static SqsExceptionArbiterReport CriticalReport()
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static SqsExceptionArbiterReport AlreadyHandledReport()
    {
        return new SqsExceptionArbiterReport
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
    public async Task RunAsync_AmazonServiceException_IsThrownRaw()
    {
        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<AmazonServiceException>())).Returns(CriticalReport());
        var sleep = CreateSleepService();
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);
        var exception = new AmazonServiceException("generic aws service failure");

        var thrown = await Assert.ThrowsAsync<AmazonServiceException>(() =>
            wrapper.RunAsync<string>(_ => throw exception, TestContext.Current.CancellationToken));

        Assert.Same(exception, thrown);
        Assert.Empty(sleep.Invocations);
    }

    [Fact]
    public async Task RunAsync_NonGeneric_WhenFuncSucceeds_CompletesWithoutSleeping()
    {
        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);

        await wrapper.RunAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken);

        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyHandled_RethrowsWithoutWrapping()
    {
        var inner = new WorkerSqsException("already wrapped")
            {CouldBeTransient = false, IsHandled = true, CouldBeExternallySolvable = true};

        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(AlreadyHandledReport());

        var sleep = CreateSleepService();
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerSqsException>(() =>
            wrapper.RunAsync(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.Same(inner, thrown);
        Assert.Equal(inner.CouldBeExternallySolvable, thrown.CouldBeExternallySolvable);
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResultWithoutSleeping()
    {
        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);

        var result = await wrapper.RunAsync(_ => Task.FromResult("ok"), TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenPermanentFailure_WrapsWithoutRetry()
    {
        var attempts = 0;
        var inner = new InvalidOperationException("permanent");

        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(PermanentReport());

        var sleep = CreateSleepService();
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerSqsException>(() => wrapper.RunAsync(
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
    public async Task RunAsync_WhenTransientFailuresExhaustRetries_WrapsAsWorkerSqsException()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var inner = new TimeoutException("still failing");

        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);

        var thrown = await Assert.ThrowsAsync<WorkerSqsException>(() => wrapper.RunAsync<string>(
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

        var arbiter = new Mock<ISqsExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());

        var sleep = CreateSleepService(delays);
        var wrapper =
            new SqsRetryWrapperService(arbiter.Object, NullLogger<SqsRetryWrapperService>.Instance, sleep.Object);

        var result = await wrapper.RunAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new TimeoutException("retry once");
                }

                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(2, attempts);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }
}