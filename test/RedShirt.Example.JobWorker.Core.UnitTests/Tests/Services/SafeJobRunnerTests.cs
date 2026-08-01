using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class SafeJobRunnerTests
{
    [Fact]
    public async Task RunSafelyAsync_WhenInternalRetryCountIsZero_DoesNotRetryJobRetryException()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result.JobSuccess);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionExhausted_RetriesThenReturnsFailureWithException()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 2
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result.JobSuccess);
        Assert.Same(expected, result.Exception);
        // initial attempt + 2 retries
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Exactly(3));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionHasExplicitDelay_OverridesIncrementalBackoff()
    {
        const int delayMilliseconds = 250;
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var attempts = 0;

        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++attempts == 1)
                {
                    throw new JobRetryException
                    {
                        DelayTimeMilliseconds = delayMilliseconds
                    };
                }

                return Task.CompletedTask;
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromMilliseconds(delayMilliseconds), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 1
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result.JobSuccess);
        Assert.Null(result.Exception);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromMilliseconds(delayMilliseconds), It.IsAny<CancellationToken>()),
            Times.Once);
        // Must not fall through to exponential backoff (2^0 = 1s).
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()), Times.Never);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionThenSucceeds_ReturnsSuccessWithNullException()
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var remainingFailures = 2;

        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (remainingFailures-- > 0)
                {
                    throw new JobRetryException();
                }

                return Task.CompletedTask;
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result.JobSuccess);
        Assert.Null(result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Exactly(3));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenLogicSucceeds_ReturnsSuccessWithNullExceptionAndDoesNotSleep()
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Strict + no DelayAsync setup: any sleep would fail the test.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result.JobSuccess);
        Assert.Null(result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenNegativeInternalRetryCount_TreatedAsZeroAndDoesNotRetry()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = -1
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result.JobSuccess);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenOrdinaryException_ReturnsFailureWithSameExceptionWithoutRetryOrSleep()
    {
        var expected = new InvalidOperationException("boom");
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        // Retries are configured, but only JobRetryException is handled — sleep must never run.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result.JobSuccess);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenRetryingWithoutExplicitDelay_UsesIncrementalBackoff()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        // Polly v8 AttemptNumber is 0-based on the failed attempt → 2^0, 2^1, 2^2.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(2), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(4), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result.JobSuccess);
        Assert.Same(expected, result.Exception);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(2), It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(4), It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}