using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Safety;

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

    private static Mock<IRawJobModel> CreateRawJob()
    {
        // Acknowledge path only needs the raw job instance for pass-through to IJobSource / failure handler.
        return new Mock<IRawJobModel>(MockBehavior.Strict);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgeSafelyAsync_WhenJobSourceSucceeds_ReturnsTrue(bool success)
    {
        var rawJobModel = CreateRawJob();

        var jobFailureHandler = new Mock<IJobFailureHandler>(MockBehavior.Strict);
        if (!success)
        {
            jobFailureHandler
                .Setup(h => h.HandleFailureAsync(rawJobModel.Object, null, TestContext.Current.CancellationToken))
                .Returns(Task.CompletedTask);
        }

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, success,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object, jobFailureHandler.Object,
            CreateSleepService(), new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, success,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, success, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenPermanentFailure_ReturnsFalseWithoutRetry()
    {
        var rawJobModel = CreateRawJob();

        var jobFailureHandler = new Mock<IJobFailureHandler>(MockBehavior.Strict);
        jobFailureHandler
            .Setup(h => h.HandleFailureAsync(rawJobModel.Object, null, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, false, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException(new Exception("permanent"), false));

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var service = new SafeJobAcknowledgementService(jobSource.Object, jobFailureHandler.Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, false, TestContext.Current.CancellationToken),
            Times.Once);
        Assert.Empty(sleepService.Invocations);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenTransientFailureThenSucceeds_RetriesAndReturnsTrue()
    {
        var rawJobModel = CreateRawJob();
        var attempts = 0;

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, true, TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new WorkerJobSourceException(new Exception($"transient {attempts}"), false, true);
                }

                return Task.CompletedTask;
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object,
            new Mock<IJobFailureHandler>(MockBehavior.Strict).Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, attempts);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(2), default), Times.Once);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(4), default), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenTransientFailuresExhaustRetries_ReturnsFalse()
    {
        var rawJobModel = CreateRawJob();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, true, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException(new Exception("transient"), false, true));

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object,
            new Mock<IJobFailureHandler>(MockBehavior.Strict).Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, true, TestContext.Current.CancellationToken),
            Times.Exactly(Globals.AcknowledgementRetryCount + 1));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(Globals.AcknowledgementRetryCount));
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenUnplannedException_BubblesUp()
    {
        var rawJobModel = CreateRawJob();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, true, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var service = new SafeJobAcknowledgementService(jobSource.Object,
            new Mock<IJobFailureHandler>(MockBehavior.Strict).Object, CreateSleepService(),
            new NullLogger<SafeJobAcknowledgementService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcknowledgeSafelyAsync(rawJobModel.Object, true,
                cancellationToken: TestContext.Current.CancellationToken));
    }
}