using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class MaintainerTests
{
    private static ISleepService CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService.Object;
    }

    /// <summary>
    ///     Confirm that getting we filter messages that cannot receive heartbeats out of the InFlight list that we received
    ///     out of the job repository.
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestFilterOutCannotHeartbeatJobs()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.CanHeartbeat).Returns(true);

        var lockId = Guid.NewGuid();
        entry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId);
        entry.Setup(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        var redHerringLockId = Guid.NewGuid();
        var redHerringEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        redHerringEntry.Setup(e => e.State).Returns(JobState.Active);
        redHerringEntry.Setup(e => e.CanHeartbeat).Returns(false);
        redHerringEntry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(redHerringLockId);
        redHerringEntry.Setup(e => e.ReleaseLockAsync(redHerringLockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>(MockBehavior.Strict);
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry.Object))
            .Returns(true);
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry.Object))
            .Returns(TimeSpan.FromSeconds(1));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry.Object,
                redHerringEntry.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken), Times.Once);

        entry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry.Verify(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken), Times.Once);

        redHerringEntry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        redHerringEntry.Verify(e => e.ReleaseLockAsync(redHerringLockId, TestContext.Current.CancellationToken),
            Times.Once);
    }

    /// <summary>
    ///     Demonstrate handling if no jobs were returned as in flight.
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatNoJobs()
    {
        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(heartbeatCalculator.Invocations);
    }

    /// <summary>
    ///     Maintain a job that is ready to heartbeat
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJob()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        entry.Setup(e => e.JobModel).Returns(subject.Object);

        var lockId = Guid.NewGuid();
        entry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId);
        entry.Setup(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry.Object))
            .Returns(true);
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry.Object))
            .Returns(TimeSpan.FromSeconds(1));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken), Times.Once);

        entry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry.Verify(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Maintain a job that is ready to heartbeat
    ///     However, a CanNoLongerHeartbeatException is thrown
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJobButGotHeartbeatException()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.SetIfFlightTimeCanBeExtendedAsync(false, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var lockId = Guid.NewGuid();
        entry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId);
        entry.Setup(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry.Object))
            .Returns(true);
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry.Object))
            .Returns(TimeSpan.FromSeconds(1));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken))
            .Returns(() => throw new CanNoLongerHeartbeatException());

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken), Times.Once);

        entry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry.Verify(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken), Times.Once);

        entry.Verify(e => e.SetIfFlightTimeCanBeExtendedAsync(false, TestContext.Current.CancellationToken),
            Times.Once);
    }

    /// <summary>
    ///     Should skip maintaining a job that is ready to heartbeat, but is already in a Complete state.
    ///     The implementation of IJobRepository should prevent this, so this is testing for an edge case.
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJob_Complete()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        entry.Setup(e => e.JobModel).Returns(subject.Object);

        var lockId = Guid.NewGuid();
        entry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId);
        entry.Setup(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Complete); // Completed

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry.Object))
            .Returns(true); // Ready for a heartbeat
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry.Object))
            .Returns(TimeSpan.FromMilliseconds(100));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
        jobSource.Verify(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken), Times.Never);

        entry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry.Verify(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Maintain a job that not yet ready for a heartbeat
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJob_NotReadyYet()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        entry.Setup(e => e.JobModel).Returns(subject.Object);

        var lockId = Guid.NewGuid();
        entry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId);
        entry.Setup(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry.Object))
            .Returns(false); // Not ready yet
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry.Object))
            .Returns(TimeSpan.FromMilliseconds(100));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);

        entry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry.Verify(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Maintain a job that is ready to heartbeat
    ///     Copy of TestHeartbeatSingleJob, with a bit more precise timing to get coverage of an edge case.
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJob_PreciseTiming()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        entry.Setup(e => e.JobModel).Returns(subject.Object);

        var lockId = Guid.NewGuid();
        entry.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId);
        entry.Setup(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry.Object))
            .Returns(true);
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry.Object))
            .Returns(TimeSpan.FromMilliseconds(100));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(subject.Object, TestContext.Current.CancellationToken), Times.Once);

        entry.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry.Verify(e => e.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Maintain two jobs that are ready to heartbeat
    ///     Expansion on TestHeartbeatSingleJob
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatTwoJob()
    {
        // First job
        var subject1 = new Mock<IJobModel>(MockBehavior.Strict);
        subject1.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry1 = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry1.Setup(e => e.JobModel).Returns(subject1.Object);

        var lockId1 = Guid.NewGuid();
        entry1.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId1);
        entry1.Setup(e => e.ReleaseLockAsync(lockId1, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry1.Setup(e => e.CanHeartbeat).Returns(true);
        entry1.Setup(e => e.State).Returns(JobState.Active);
        entry1.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        // Second job
        var subject2 = new Mock<IJobModel>(MockBehavior.Strict);
        subject2.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry2 = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry2.Setup(e => e.JobModel).Returns(subject2.Object);

        var lockId2 = Guid.NewGuid();
        entry2.Setup(e => e.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(lockId2);
        entry2.Setup(e => e.ReleaseLockAsync(lockId2, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry2.Setup(e => e.State).Returns(JobState.Active);
        entry2.Setup(e => e.CanHeartbeat).Returns(true);
        entry2.SetupSet(e => e.LastHeartbeatTime = It.Is<DateTime>(dt =>
            dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
            dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)));

        // Heartbeat
        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry1.Object))
            .Returns(true);
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry1.Object))
            .Returns(TimeSpan.FromMilliseconds(100));
        heartbeatCalculator
            .Setup(c => c.IsReadyForHeartbeat(entry2.Object))
            .Returns(true);
        heartbeatCalculator
            .Setup(c => c.TimeUntilNextHeartbeat(entry2.Object))
            .Returns(TimeSpan.FromMilliseconds(100));

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
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
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([
                entry1.Object,
                entry2.Object
            ]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(subject1.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);
        jobSource
            .Setup(s => s.HeartbeatAsync(subject2.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var maintainer = new Maintainer(heartbeatCalculator.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object, new NullLogger<Maintainer>(), CreateSleepService());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(subject1.Object, TestContext.Current.CancellationToken), Times.Once);
        jobSource.Verify(s => s.HeartbeatAsync(subject2.Object, TestContext.Current.CancellationToken), Times.Once);

        entry1.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry1.Verify(e => e.ReleaseLockAsync(lockId1, TestContext.Current.CancellationToken), Times.Once);

        entry2.Verify(e => e.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        entry2.Verify(e => e.ReleaseLockAsync(lockId2, TestContext.Current.CancellationToken), Times.Once);
    }
}