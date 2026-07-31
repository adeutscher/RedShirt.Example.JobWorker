using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Exceptions.JobSource;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class SafeJobAcknowledgementServiceTests
{
    private static ISleepService CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService.Object;
    }

    private static (Mock<IJobRepositoryEntry> Entry, Mock<IJobModel> JobModel) CreateJob()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(jobModel.Object);
        return (entry, jobModel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgeSafelyAsync_WhenJobSourceSucceeds_ReturnsTrue(bool success)
    {
        var (entry, jobModel) = CreateJob();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeCompletionAsync(jobModel.Object, success, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object, CreateSleepService(),
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(entry.Object, success, TestContext.Current.CancellationToken);

        Assert.True(result);
        jobSource.Verify(
            s => s.AcknowledgeCompletionAsync(jobModel.Object, success, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenPermanentFailure_ReturnsFalseWithoutRetry()
    {
        var (entry, jobModel) = CreateJob();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeCompletionAsync(jobModel.Object, false, TestContext.Current.CancellationToken))
            .ThrowsAsync(new JobSourceAcknowledgementException(false, new Exception("permanent")));

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var service = new SafeJobAcknowledgementService(jobSource.Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(entry.Object, false, TestContext.Current.CancellationToken);

        Assert.False(result);
        jobSource.Verify(
            s => s.AcknowledgeCompletionAsync(jobModel.Object, false, TestContext.Current.CancellationToken),
            Times.Once);
        Assert.Empty(sleepService.Invocations);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenTransientFailureThenSucceeds_RetriesAndReturnsTrue()
    {
        var (entry, jobModel) = CreateJob();
        var attempts = 0;

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeCompletionAsync(jobModel.Object, true, TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new JobSourceAcknowledgementException(true, new Exception($"transient {attempts}"));
                }

                return Task.CompletedTask;
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(3, attempts);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(2), default), Times.Once);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(4), default), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenTransientFailuresExhaustRetries_ReturnsFalse()
    {
        var (entry, jobModel) = CreateJob();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeCompletionAsync(jobModel.Object, true, TestContext.Current.CancellationToken))
            .ThrowsAsync(new JobSourceAcknowledgementException(true, new Exception("transient")));

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken);

        Assert.False(result);
        // Initial attempt + Globals.AcknowledgementRetryCount retries
        jobSource.Verify(
            s => s.AcknowledgeCompletionAsync(jobModel.Object, true, TestContext.Current.CancellationToken),
            Times.Exactly(Globals.AcknowledgementRetryCount + 1));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(Globals.AcknowledgementRetryCount));
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenUnplannedException_BubblesUp()
    {
        var (entry, jobModel) = CreateJob();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeCompletionAsync(jobModel.Object, true, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var service = new SafeJobAcknowledgementService(jobSource.Object, CreateSleepService(),
            new NullLogger<SafeJobAcknowledgementService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken));
    }
}