using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Idempotency;

public class IdempotencyMonitorTests
{
    private static IdempotencyConfigurationModel CreateOptions(bool enabled = true, int monitorIntervalSeconds = 5)
    {
        return new IdempotencyConfigurationModel
        {
            Enabled = enabled,
            ResultCacheDurationSeconds = 30,
            MonitorIntervalSeconds = monitorIntervalSeconds,
            IdempotencyIdsCanRepeat = false
        };
    }

    private static Mock<IAbstractedLock> CreateLock(bool isAcquired)
    {
        var idempotencyLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        idempotencyLock.SetupGet(l => l.IsAcquired).Returns(isAcquired);
        idempotencyLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return idempotencyLock;
    }

    private static (Mock<IJobRepositoryEntry> Entry, Mock<IJobModel> JobModel, Mock<IRawJobModel> RawJobModel)
        CreateBlockedJob()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(jobModel.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        return (entry, jobModel, rawJobModel);
    }

    private static ISleepService CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService.Object;
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_SleepsUsingEffectiveMonitorIntervalBetweenLoops()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var doQuit = false;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
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
            .Setup(r => r.GetAllIdempotencyBlockedJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([]);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            new Mock<IIdempotencyExecutionService>(MockBehavior.Strict).Object,
            new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict).Object, sleepService.Object,
            Options.Create(CreateOptions(monitorIntervalSeconds: 1)), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory(Timeout = 2000)]
    [InlineData(null)]
    [InlineData(CoreJobResult.Failure)]
    public async Task RunAsync_WhenCachedResultIsNullOrUnsuccessful_ReloadsUnblockedJob(CoreJobResult? jobResult)
    {
        var (entry, jobModel, _) = CreateBlockedJob();
        var idempotencyLock = CreateLock(true);
        var cachedResult = jobResult switch
        {
            null => null,
            { } result => new IdempotencyCacheResult
            {
                JobResult = result,
                AcknowledgementResult = new SafeAcknowledgementResult
                {
                    AcknowledgedSuccessfully = true,
                    LoggedFailureSuccessfully = null
                }
            }
        };

        var doQuit = false;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
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
            .Setup(r => r.GetAllIdempotencyBlockedJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([entry.Object]);
        jobRepository
            .Setup(r => r.ReloadUnblockedJobAsync(entry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedResult);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict).Object,
            CreateSleepService(), Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        jobRepository.Verify(r => r.ReloadUnblockedJobAsync(entry.Object, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenCachedResultIsSuccessAndAcknowledgeFails_RemovesJobWithoutRefreshingCache()
    {
        var (entry, jobModel, rawJobModel) = CreateBlockedJob();
        var idempotencyLock = CreateLock(true);
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

        var doQuit = false;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
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
            .Setup(r => r.GetAllIdempotencyBlockedJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([entry.Object]);
        jobRepository
            .Setup(r => r.RemoveJobAsync(entry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedResult);

        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeJobAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success, null,
                cachedResult.AcknowledgementResult,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(failedAck);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        safeJobAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success, null,
                cachedResult.AcknowledgementResult,
                TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(It.IsAny<IRawJobModel>(), It.IsAny<CoreJobResult>(),
                It.IsAny<ISafeAcknowledgementResult>(),
                It.IsAny<CancellationToken>()), Times.Never);
        jobRepository.Verify(r => r.RemoveJobAsync(entry.Object, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenCachedResultIsSuccessAndAcknowledgeSucceeds_RemovesJobAndRefreshesCache()
    {
        var (entry, jobModel, rawJobModel) = CreateBlockedJob();
        var idempotencyLock = CreateLock(true);
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

        var doQuit = false;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
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
            .Setup(r => r.GetAllIdempotencyBlockedJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([entry.Object]);
        jobRepository
            .Setup(r => r.RemoveJobAsync(entry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedResult);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(rawJobModel.Object, CoreJobResult.Success,
                cachedResult.AcknowledgementResult,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeJobAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(rawJobModel.Object, CoreJobResult.Success, null,
                cachedResult.AcknowledgementResult,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(successAck);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(rawJobModel.Object, CoreJobResult.Success,
                cachedResult.AcknowledgementResult,
                TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(entry.Object, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenDisabled_ReturnsImmediately()
    {
        var monitor = new IdempotencyMonitor(new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IIdempotencyExecutionService>(MockBehavior.Strict).Object,
            new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict).Object, CreateSleepService(),
            Options.Create(CreateOptions(false)), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenLockNotAcquired_LeavesJobBlocked()
    {
        var (entry, jobModel, _) = CreateBlockedJob();
        var idempotencyLock = CreateLock(false);

        var doQuit = false;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
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
            .Setup(r => r.GetAllIdempotencyBlockedJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([entry.Object]);

        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict).Object,
            CreateSleepService(), Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        idempotencyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
        jobRepository.Verify(
            r => r.ReloadUnblockedJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.RemoveJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}