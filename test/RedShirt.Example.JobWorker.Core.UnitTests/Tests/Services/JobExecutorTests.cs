using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class JobExecutorTests
{
    private static Mock<IAbstractedLock> CreateAcquiredIdempotencyLock()
    {
        var idempotencyLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        idempotencyLock.SetupGet(l => l.IsAcquired).Returns(true);
        idempotencyLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return idempotencyLock;
    }

    [Theory(Timeout = 2000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteSingleJob(bool safeRunnerSuccess)
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var jobRepositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobRepositoryEntry.Setup(j => j.JobModel).Returns(jobModel.Object);
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, safeRunnerSuccess,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
            {
                // ReSharper disable once InvertIf
                if (!doQuit)
                {
                    // Should only be invoked twice
                    // First to tee up the exit, and a second time to exit
                    doQuit = true;
                    return true;
                }

                return false;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        safeJobRunner.Setup(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(safeRunnerSuccess);

        var idempotencyLock = CreateAcquiredIdempotencyLock();
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync((bool?) null);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(jobModel.Object, safeRunnerSuccess, true,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        Assert.Single(safeJobRunner.Invocations);
        safeJobRunner.Verify(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);

        Assert.Single(safeAcknowledgementService.Invocations);
        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, safeRunnerSuccess,
                TestContext.Current.CancellationToken),
            Times.Once);

        jobRepositoryEntry.Verify(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken),
            Times.Once);
        idempotencyExecutionService.Verify(
            s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(jobModel.Object, safeRunnerSuccess, true,
                TestContext.Current.CancellationToken), Times.Once);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 500)]
    public async Task PrepareToExitOnNull()
    {
        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
            {
                if (doQuit)
                {
                    return false;
                }

                doQuit = true;
                return true;
            });

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((IJobRepositoryEntry?) null);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        Assert.Empty(safeJobRunner.Invocations);
        Assert.Empty(safeAcknowledgementService.Invocations);
        Assert.Empty(idempotencyExecutionService.Invocations);

        Assert.Single(jobRepository.Invocations);
        jobRepository.Verify(r => r.GetNextJobAsync(TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory(Timeout = 2000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenCachedResultIsFalse_RunsJobAsRetry(bool safeRunnerSuccess)
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var jobRepositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobRepositoryEntry.Setup(j => j.JobModel).Returns(jobModel.Object);
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, safeRunnerSuccess,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        safeJobRunner.Setup(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(safeRunnerSuccess);

        var idempotencyLock = CreateAcquiredIdempotencyLock();
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(false);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(jobModel.Object, safeRunnerSuccess, true,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeJobRunner.Verify(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);
        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, safeRunnerSuccess,
                TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(jobModel.Object, safeRunnerSuccess, true,
                TestContext.Current.CancellationToken), Times.Once);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task WhenCachedResultIsTrueAndAcknowledgeFails_SkipsExecution()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var jobRepositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobRepositoryEntry.Setup(j => j.JobModel).Returns(jobModel.Object);

        var idempotencyLock = CreateAcquiredIdempotencyLock();

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(jobModel.Object, true, false, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, true,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(false);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, true, TestContext.Current.CancellationToken),
            Times.Once);
        Assert.Empty(safeJobRunner.Invocations);
        jobRepository.Verify(r => r.RemoveJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(It.IsAny<IJobModel>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(jobModel.Object, true, false, TestContext.Current.CancellationToken),
            Times.Once);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task WhenCachedResultIsTrueAndAcknowledgeSucceeds_SkipsExecution()
    {
        // Documents current ActOnJobAsync behaviour: a successful cached-result acknowledgement does not return early.
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var jobRepositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobRepositoryEntry.Setup(j => j.JobModel).Returns(jobModel.Object);
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, true,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        safeJobRunner.Setup(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var idempotencyLock = CreateAcquiredIdempotencyLock();
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(jobModel.Object, true, true, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(jobRepositoryEntry.Object, true, TestContext.Current.CancellationToken),
            Times.Once);
        safeJobRunner.Verify(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken),
            Times.Never);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(jobModel.Object, true, true, TestContext.Current.CancellationToken),
            Times.Once);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task WhenIdempotencyLockNotAcquired_MarksJobBlockedAndContinues()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var jobRepositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobRepositoryEntry.Setup(j => j.JobModel).Returns(jobModel.Object);
        jobRepositoryEntry
            .Setup(j => j.SetStateAsync(JobState.BlockedByIdempotency, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        idempotencyLock.SetupGet(l => l.IsAcquired).Returns(false);
        idempotencyLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        jobRepositoryEntry.Verify(
            j => j.SetStateAsync(JobState.BlockedByIdempotency, TestContext.Current.CancellationToken), Times.Once);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(safeJobRunner.Invocations);
        Assert.Empty(safeAcknowledgementService.Invocations);
        idempotencyExecutionService.Verify(
            s => s.GetCachedResultAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}