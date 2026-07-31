using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;

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
        idempotencyLock.Setup(l => l.Unlock());
        return idempotencyLock;
    }

    private static (Mock<IJobRepositoryEntry> Entry, Mock<IJobModel> JobModel) CreateBlockedJob()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(jobModel.Object);
        return (entry, jobModel);
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
    [InlineData(false)]
    public async Task RunAsync_WhenCachedResultIsNullOrFalse_ReloadsUnblockedJob(bool? cachedResult)
    {
        var (entry, jobModel) = CreateBlockedJob();
        var idempotencyLock = CreateLock(true);

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

        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        jobRepository.Verify(r => r.ReloadUnblockedJobAsync(entry.Object, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(safeJobAcknowledgementService.Invocations);
        idempotencyLock.Verify(l => l.Unlock(), Times.Once);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenCachedResultIsTrueAndAcknowledgeFails_RemovesJobWithoutRefreshingCache()
    {
        var (entry, jobModel) = CreateBlockedJob();
        var idempotencyLock = CreateLock(true);

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
            .ReturnsAsync(true);

        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeJobAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken))
            .ReturnsAsync(false);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        safeJobAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(It.IsAny<IJobModel>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        jobRepository.Verify(r => r.RemoveJobAsync(entry.Object, TestContext.Current.CancellationToken), Times.Once);
        idempotencyLock.Verify(l => l.Unlock(), Times.Once);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenCachedResultIsTrueAndAcknowledgeSucceeds_RemovesJobAndRefreshesCache()
    {
        var (entry, jobModel) = CreateBlockedJob();
        var idempotencyLock = CreateLock(true);

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
            .ReturnsAsync(true);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(jobModel.Object, true, true, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        safeJobAcknowledgementService
            .Setup(s => s.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        safeJobAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(entry.Object, true, TestContext.Current.CancellationToken), Times.Once);
        idempotencyExecutionService.Verify(
            s => s.SetResultInCacheAsync(jobModel.Object, true, true, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(r => r.RemoveJobAsync(entry.Object, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(
            r => r.ReloadUnblockedJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotencyLock.Verify(l => l.Unlock(), Times.Once);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenDisabled_ReturnsImmediately()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions(false)), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(executionEndArbiter.Invocations);
        Assert.Empty(jobRepository.Invocations);
        Assert.Empty(idempotencyExecutionService.Invocations);
        Assert.Empty(safeJobAcknowledgementService.Invocations);
    }

    [Fact(Timeout = 1000)]
    public async Task RunAsync_WhenLockNotAcquired_LeavesJobBlocked()
    {
        var (entry, jobModel) = CreateBlockedJob();
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

        var safeJobAcknowledgementService = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);

        var monitor = new IdempotencyMonitor(executionEndArbiter.Object, jobRepository.Object,
            idempotencyExecutionService.Object, safeJobAcknowledgementService.Object, CreateSleepService(),
            Options.Create(CreateOptions()), new NullLogger<IdempotencyMonitor>());

        await monitor.RunAsync(TestContext.Current.CancellationToken);

        idempotencyLock.Verify(l => l.Unlock(), Times.Once);
        jobRepository.Verify(
            r => r.ReloadUnblockedJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.RemoveJobAsync(It.IsAny<IJobRepositoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(safeJobAcknowledgementService.Invocations);
    }
}