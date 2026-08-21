using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs;

public class JobExecutorTests
{
    private static ICoreStatisticsService CreateStatisticsService()
    {
        var statistics = new Mock<ICoreStatisticsService>(MockBehavior.Strict);
        statistics.Setup(s => s.RecordReceived());
        statistics.Setup(s => s.RecordResult(It.IsAny<CoreJobResult>(), It.IsAny<TimeSpan>()));
        return statistics.Object;
    }

    private static Mock<IAbstractedLock> CreateAcquiredIdempotencyLock()
    {
        var idempotencyLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        idempotencyLock.SetupGet(l => l.IsAcquired).Returns(true);
        idempotencyLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return idempotencyLock;
    }

    private static (Mock<IJobRepositoryEntry> Entry, Mock<IJobModel> JobModel, Mock<IRawJobModel> RawJobModel)
        CreateRepositoryEntry()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(j => j.JobModel).Returns(jobModel.Object);
        entry.Setup(j => j.RawJobModel).Returns(rawJobModel.Object);
        return (entry, jobModel, rawJobModel);
    }

    [Theory(Timeout = 2000)]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task ExecuteSingleJob(CoreJobResult safeRunnerResult)
    {
        var (jobRepositoryEntry, jobModel, rawJobModel) = CreateRepositoryEntry();
        var ackResult = new SafeAcknowledgementResult
        {
            AcknowledgedSuccessfully = true,
            LoggedFailureSuccessfully = null
        };
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var runException = safeRunnerResult == CoreJobResult.Success
            ? null
            : new InvalidOperationException("job failed");
        var safeJobResult = new SafeJobRunResults
        {
            Result = safeRunnerResult,
            Exception = runException
        };

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(rawJobModel.Object, safeRunnerResult, runException, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ackResult);

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutorExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunning())
            .Returns(() =>
            {
                if (!doQuit)
                {
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
            .ReturnsAsync(safeJobResult);

        var idempotencyLock = CreateAcquiredIdempotencyLock();
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync((IdempotencyCacheResult?) null);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(rawJobModel.Object, safeRunnerResult, ackResult,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            CreateStatisticsService(),
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeJobRunner.Verify(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);
        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(rawJobModel.Object, safeRunnerResult, runException, null,
                TestContext.Current.CancellationToken), Times.Once);
        jobRepositoryEntry.Verify(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken),
            Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(rawJobModel.Object, safeRunnerResult, ackResult,
                TestContext.Current.CancellationToken), Times.Once);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 500)]
    public async Task PrepareToExitOnNull()
    {
        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutorExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunning())
            .Returns(() =>
            {
                if (doQuit)
                {
                    return false;
                }

                doQuit = true;
                return true;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((IJobRepositoryEntry?) null);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            new Mock<IIdempotencyExecutionService>(MockBehavior.Strict).Object,
            new Mock<ISafeJobRunner>(MockBehavior.Strict).Object,
            new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict).Object,
            CreateStatisticsService(),
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        jobRepository.Verify(r => r.GetNextJobAsync(TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task WhenCachedResultIsSuccessAndAcknowledgeFails_SkipsExecution()
    {
        var (jobRepositoryEntry, jobModel, rawJobModel) = CreateRepositoryEntry();
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        var cachedResult = new IdempotencyCacheResult
        {
            JobResult = CoreJobResult.Success,
            AcknowledgementResult = new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            }
        };
        var failedAck = new SafeAcknowledgementResult
        {
            AcknowledgedSuccessfully = false,
            LoggedFailureSuccessfully = null
        };

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutorExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunning())
            .Returns(() => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(CreateAcquiredIdempotencyLock().Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedResult);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(rawJobModel.Object, CoreJobResult.Success, failedAck,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success, null, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(failedAck);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, new Mock<ISafeJobRunner>(MockBehavior.Strict).Object,
            safeAcknowledgementService.Object, CreateStatisticsService(), new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success, null, null,
                TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(rawJobModel.Object, CoreJobResult.Success, failedAck,
                TestContext.Current.CancellationToken),
            Times.Once);
        jobRepositoryEntry.Verify(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task WhenCachedResultIsSuccessAndAcknowledgeSucceeds_SkipsExecution()
    {
        var (jobRepositoryEntry, jobModel, rawJobModel) = CreateRepositoryEntry();
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        var cachedResult = new IdempotencyCacheResult
        {
            JobResult = CoreJobResult.Success,
            AcknowledgementResult = new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            }
        };
        var successAck = new SafeAcknowledgementResult
        {
            AcknowledgedSuccessfully = true,
            LoggedFailureSuccessfully = null
        };

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutorExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunning())
            .Returns(() => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(CreateAcquiredIdempotencyLock().Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedResult);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(rawJobModel.Object, CoreJobResult.Success, successAck,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success, null, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(successAck);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            CreateStatisticsService(),
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeJobRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(rawJobModel.Object, CoreJobResult.Success, successAck,
                TestContext.Current.CancellationToken),
            Times.Once);
        jobRepositoryEntry.Verify(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory(Timeout = 2000)]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task WhenCachedResultIsUnsuccessful_RunsJobAsRetry(CoreJobResult safeRunnerResult)
    {
        var (jobRepositoryEntry, jobModel, rawJobModel) = CreateRepositoryEntry();
        var ackResult = new SafeAcknowledgementResult
        {
            AcknowledgedSuccessfully = true,
            LoggedFailureSuccessfully = null
        };
        jobRepositoryEntry.Setup(j => j.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var runException = safeRunnerResult == CoreJobResult.Success
            ? null
            : new InvalidOperationException("job failed");
        var safeJobResult = new SafeJobRunResults
        {
            Result = safeRunnerResult,
            Exception = runException
        };

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(rawJobModel.Object, safeRunnerResult, runException, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ackResult);

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutorExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunning())
            .Returns(() => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        safeJobRunner.Setup(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(safeJobResult);

        var idempotencyLock = CreateAcquiredIdempotencyLock();
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(new IdempotencyCacheResult
            {
                JobResult = CoreJobResult.Failure,
                AcknowledgementResult = new SafeAcknowledgementResult
                {
                    AcknowledgedSuccessfully = true,
                    LoggedFailureSuccessfully = null
                }
            });
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(rawJobModel.Object, safeRunnerResult, ackResult,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobRunner.Object, safeAcknowledgementService.Object,
            CreateStatisticsService(),
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        safeJobRunner.Verify(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(rawJobModel.Object, safeRunnerResult, ackResult,
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task WhenIdempotencyLockNotAcquired_MarksJobBlockedAndContinues()
    {
        var (jobRepositoryEntry, jobModel, _) = CreateRepositoryEntry();
        jobRepositoryEntry
            .Setup(j => j.SetStateAsync(JobState.BlockedByIdempotency, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        idempotencyLock.SetupGet(l => l.IsAcquired).Returns(false);
        idempotencyLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var calls = 0;
        var executionEndArbiter = new Mock<IAppliedExecutorExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunning())
            .Returns(() => ++calls <= 1);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, new Mock<ISafeJobRunner>(MockBehavior.Strict).Object,
            new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict).Object, CreateStatisticsService(),
            new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        jobRepositoryEntry.Verify(
            j => j.SetStateAsync(JobState.BlockedByIdempotency, TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.GetCachedResultAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}