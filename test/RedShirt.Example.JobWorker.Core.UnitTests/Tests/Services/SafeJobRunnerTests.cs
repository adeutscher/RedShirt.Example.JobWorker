using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class SafeJobRunnerTests
{
    private static Mock<ISleepService> CreateSleepService(List<TimeSpan>? delays = null)
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<TimeSpan, CancellationToken>((delay, _) => delays?.Add(delay));
        return sleepService;
    }

    private static SafeJobRunner CreateRunner(IJobLogicRunner logicRunner, ISleepService sleepService,
        int internalRetryCount = 0)
    {
        return new SafeJobRunner(logicRunner, sleepService, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = internalRetryCount
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RunSafelyAsync_WhenJobRetryExceptionExhausted_ReturnsFalse(int internalRetryCount)
    {
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var job = TestJobHelpers.CreateJobModel();

        logicRunner
            .Setup(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken))
            .ThrowsAsync(new JobRetryException());

        var result = await CreateRunner(logicRunner.Object, sleepService.Object, internalRetryCount)
            .RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result);
        logicRunner.Verify(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken),
            Times.Exactly(internalRetryCount + 1));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(internalRetryCount));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionHasExplicitDelay_UsesThatDelay()
    {
        const int delayMilliseconds = 250;

        var delays = new List<TimeSpan>();
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        var sleepService = CreateSleepService(delays);
        var job = TestJobHelpers.CreateJobModel();
        var attempts = 0;

        logicRunner
            .Setup(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new JobRetryException
                    {
                        DelayTimeMilliseconds = delayMilliseconds
                    };
                }

                return Task.CompletedTask;
            });

        var result = await CreateRunner(logicRunner.Object, sleepService.Object, 1)
            .RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal([TimeSpan.FromMilliseconds(delayMilliseconds)], delays);
        logicRunner.Verify(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionThenSucceeds_ReturnsTrue()
    {
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var job = TestJobHelpers.CreateJobModel();
        var remainingFailures = 2;

        logicRunner
            .Setup(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                if (remainingFailures > 0)
                {
                    remainingFailures--;
                    throw new JobRetryException();
                }

                return Task.CompletedTask;
            });

        var result = await CreateRunner(logicRunner.Object, sleepService.Object, 3)
            .RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result);
        logicRunner.Verify(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken), Times.Exactly(3));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenLogicSucceeds_ReturnsTrueAndInvokesOnce()
    {
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var job = TestJobHelpers.CreateJobModel();

        logicRunner
            .Setup(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var result = await CreateRunner(logicRunner.Object, sleepService.Object)
            .RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result);
        logicRunner.Verify(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken), Times.Once);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenOrdinaryException_ReturnsFalseWithoutRetry()
    {
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        var sleepService = CreateSleepService();
        var job = TestJobHelpers.CreateJobModel();

        logicRunner
            .Setup(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateRunner(logicRunner.Object, sleepService.Object, 3)
            .RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result);
        logicRunner.Verify(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken), Times.Once);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenRetryingWithoutExplicitDelay_UsesIncrementalBackoff()
    {
        var delays = new List<TimeSpan>();
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        var sleepService = CreateSleepService(delays);
        var job = TestJobHelpers.CreateJobModel();

        logicRunner
            .Setup(l => l.RunAsync(job.Object, TestContext.Current.CancellationToken))
            .ThrowsAsync(new JobRetryException());

        var result = await CreateRunner(logicRunner.Object, sleepService.Object, 3)
            .RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal(
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8)
        ], delays);
    }
}