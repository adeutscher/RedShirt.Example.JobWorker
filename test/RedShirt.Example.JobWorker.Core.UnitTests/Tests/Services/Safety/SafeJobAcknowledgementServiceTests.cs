using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Safety;

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
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task AcknowledgeSafelyAsync_WhenJobSourceSucceeds_ReturnsTrue(CoreJobResult result)
    {
        var rawJobModel = CreateRawJob();

        var jobFailureHandler = new Mock<IJobFailureHandler>(MockBehavior.Strict);
        if (result != CoreJobResult.Success)
        {
            jobFailureHandler
                .Setup(h => h.HandleFailureAsync(rawJobModel.Object, FailureType.Execution, null,
                    TestContext.Current.CancellationToken))
                .Returns(Task.CompletedTask);
        }

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, result,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object, jobFailureHandler.Object,
            CreateSleepService(), new NullLogger<SafeJobAcknowledgementService>());

        var ackResult = await service.AcknowledgeSafelyAsync(rawJobModel.Object, result,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(ackResult.Success);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, result, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenPermanentFailure_ReturnsFalseWithoutRetry()
    {
        var rawJobModel = CreateRawJob();

        var jobFailureHandler = new Mock<IJobFailureHandler>(MockBehavior.Strict);
        jobFailureHandler
            .Setup(h => h.HandleFailureAsync(rawJobModel.Object, FailureType.Execution, null,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Failure,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException(new Exception("permanent"))
                {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false});

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var service = new SafeJobAcknowledgementService(jobSource.Object, jobFailureHandler.Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Failure,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Failure,
                TestContext.Current.CancellationToken),
            Times.Once);
        Assert.Empty(sleepService.Invocations);
    }

    /// <summary>
    ///     Micro-idempotency: if a previous attempt already logged the failure, a retry must not invoke
    ///     <see cref="IJobFailureHandler" /> again.
    /// </summary>
    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenPreviousAttemptLoggedFailure_SkipsFailureHandler()
    {
        var rawJobModel = CreateRawJob();
        var exception = new Exception("job failed");

        var jobFailureHandler = new Mock<IJobFailureHandler>(MockBehavior.Strict);
        jobFailureHandler
            .Setup(h => h.HandleFailureAsync(rawJobModel.Object, FailureType.Execution, exception,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var acknowledgeAttempts = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Failure,
                TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                acknowledgeAttempts++;
                // ReSharper disable once ConvertIfStatementToReturnStatement
                if (acknowledgeAttempts == 1)
                {
                    throw new WorkerJobSourceException(new Exception("ack failed"))
                        {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};
                }

                return Task.CompletedTask;
            });

        var service = new SafeJobAcknowledgementService(jobSource.Object, jobFailureHandler.Object,
            CreateSleepService(), new NullLogger<SafeJobAcknowledgementService>());

        var firstAttempt = await service.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Failure, exception,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(firstAttempt.LoggedFailureSuccessfully);
        Assert.False(firstAttempt.AcknowledgedSuccessfully);

        var secondAttempt = await service.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Failure, exception,
            firstAttempt, TestContext.Current.CancellationToken);

        Assert.True(secondAttempt.Success);
        jobFailureHandler.Verify(
            h => h.HandleFailureAsync(rawJobModel.Object, FailureType.Execution, exception,
                TestContext.Current.CancellationToken),
            Times.Once);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Failure,
                TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }

    [Fact]
    public async Task AcknowledgeSafelyAsync_WhenTransientFailureThenSucceeds_RetriesAndReturnsTrue()
    {
        var rawJobModel = CreateRawJob();
        var attempts = 0;

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Success,
                TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new WorkerJobSourceException(new Exception($"transient {attempts}"))
                        {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true};
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

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success,
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
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Success,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException(new Exception("transient"))
                {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true});

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SafeJobAcknowledgementService(jobSource.Object,
            new Mock<IJobFailureHandler>(MockBehavior.Strict).Object, sleepService.Object,
            new NullLogger<SafeJobAcknowledgementService>());

        var result = await service.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Success,
                TestContext.Current.CancellationToken),
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
            .Setup(s => s.AcknowledgeAsync(rawJobModel.Object, CoreJobResult.Success,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var service = new SafeJobAcknowledgementService(jobSource.Object,
            new Mock<IJobFailureHandler>(MockBehavior.Strict).Object, CreateSleepService(),
            new NullLogger<SafeJobAcknowledgementService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success,
                cancellationToken: TestContext.Current.CancellationToken));
    }
}