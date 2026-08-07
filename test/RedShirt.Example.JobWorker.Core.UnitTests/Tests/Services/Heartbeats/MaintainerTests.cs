using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Heartbeats;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Heartbeats;

public class HeartbeatMaintainerTests
{
    private static ICoreHealthStateUpdateService CreateHealthStateUpdateService()
    {
        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());
        return health.Object;
    }

    private static ISleepService CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService.Object;
    }

    [Theory(Timeout = 500)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RunAsync_WhenRecommendedHeartbeatIntervalIsNotPositive_ReturnsImmediately(int intervalSeconds)
    {
        var heartbeatCalculator = new Mock<IHeartbeatCalculator>(MockBehavior.Strict);
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(intervalSeconds);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(executionEndArbiter.Invocations);
        Assert.Empty(jobRepository.Invocations);
        Assert.Empty(heartbeatCalculator.Invocations);
    }

    [Fact(Timeout = 1500)]
    public async Task RunAsync_WhenUnexpectedHeartbeatException_AndHaltOnFailureFalse_MarksCannotHeartbeat()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator.Setup(c => c.IsReadyForHeartbeat(entry.Object)).Returns(true);
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
            .ReturnsAsync([entry.Object]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("auth failed"));

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, health.Object,
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        health.Verify(h => h.NoteIncident(), Times.Once);
        entry.Verify(e => e.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 1500)]
    public async Task RunAsync_WhenUnexpectedHeartbeatException_AndHaltOnFailure_Propagates()
    {
        var unexpected = new InvalidOperationException("auth failed");

        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.State).Returns(JobState.Active);

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>();
        heartbeatCalculator.Setup(c => c.IsReadyForHeartbeat(entry.Object)).Returns(true);

        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(true);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync([entry.Object]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, health.Object,
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            new NullLogger<HeartbeatMaintainer>());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            maintainer.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
        entry.Verify(e => e.SetAsCannotHeartbeatAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        var redHerringEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        redHerringEntry.Setup(e => e.State).Returns(JobState.Active);
        redHerringEntry.Setup(e => e.CanHeartbeat).Returns(false);

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
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken), Times.Once);
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

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IRawJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

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
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken), Times.Once);
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
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

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
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .Returns(() => throw new WorkerJobSourceException("Test")
                {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false});

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken), Times.Once);

        entry.Verify(e => e.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken),
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
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
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

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IRawJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken), Times.Never);
    }

    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJob_ExhaustsTransientRetriesThenDisablesExtension()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        entry.Setup(e => e.State).Returns(JobState.Active);

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>(MockBehavior.Strict);
        heartbeatCalculator.Setup(c => c.IsReadyForHeartbeat(entry.Object)).Returns(true);
        heartbeatCalculator.Setup(c => c.TimeUntilNextHeartbeat(entry.Object)).Returns(TimeSpan.FromSeconds(1));

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
            .ReturnsAsync([entry.Object]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException(new Exception("transient"))
                {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true});

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            sleepService.Object,
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken),
            Times.Exactly(Globals.HeartbeatRetryCount + 1));
        entry.Verify(e => e.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken),
            Times.Once);
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
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

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

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IRawJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

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
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 1500)]
    public async Task TestHeartbeatSingleJob_RetriesTransientFailuresThenSucceeds()
    {
        var subject = new Mock<IJobModel>(MockBehavior.Strict);
        subject.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var entry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry.Setup(e => e.CanHeartbeat).Returns(true);
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict);
        entry.Setup(e => e.JobModel).Returns(subject.Object);
        entry.Setup(e => e.RawJobModel).Returns(rawJobModel.Object);
        entry.Setup(e => e.State).Returns(JobState.Active);
        entry.Setup(e => e.SetLastHeartbeatTimeAsync(It.IsAny<DateTime>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var heartbeatCalculator = new Mock<IHeartbeatCalculator>(MockBehavior.Strict);
        heartbeatCalculator.Setup(c => c.IsReadyForHeartbeat(entry.Object)).Returns(true);
        heartbeatCalculator.Setup(c => c.TimeUntilNextHeartbeat(entry.Object)).Returns(TimeSpan.FromSeconds(1));

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
            .ReturnsAsync([entry.Object]);

        var attempts = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);
        jobSource
            .Setup(s => s.HeartbeatAsync(rawJobModel.Object, TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new WorkerJobSourceException(new Exception($"transient {attempts}"))
                    {
                        CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true
                    };
                }

                return Task.CompletedTask;
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            sleepService.Object,
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, attempts);
        entry.Verify(e => e.SetAsCannotHeartbeatAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        entry.Verify(e => e.SetLastHeartbeatTimeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var rawJobModel1 = new Mock<IRawJobModel>(MockBehavior.Strict);
        var entry1 = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry1.Setup(e => e.JobModel).Returns(subject1.Object);
        entry1.Setup(e => e.RawJobModel).Returns(rawJobModel1.Object);
        entry1.Setup(e => e.CanHeartbeat).Returns(true);
        entry1.Setup(e => e.State).Returns(JobState.Active);
        entry1.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Second job
        var subject2 = new Mock<IJobModel>(MockBehavior.Strict);
        subject2.Setup(s => s.MessageId).Returns(Guid.NewGuid().ToString());
        var rawJobModel2 = new Mock<IRawJobModel>(MockBehavior.Strict);
        var entry2 = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        entry2.Setup(e => e.JobModel).Returns(subject2.Object);
        entry2.Setup(e => e.RawJobModel).Returns(rawJobModel2.Object);
        entry2.Setup(e => e.State).Returns(JobState.Active);
        entry2.Setup(e => e.CanHeartbeat).Returns(true);
        entry2.Setup(e => e.SetLastHeartbeatTimeAsync(It.Is<DateTime>(dt =>
                dt > DateTime.UtcNow - TimeSpan.FromMilliseconds(250) &&
                dt < DateTime.UtcNow + TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

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
            .Setup(s => s.HeartbeatAsync(rawJobModel1.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobSource
            .Setup(s => s.HeartbeatAsync(rawJobModel2.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var maintainer = new HeartbeatMaintainer(heartbeatCalculator.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object, CreateHealthStateUpdateService(),
            CreateSleepService(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), new NullLogger<HeartbeatMaintainer>());

        await maintainer.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobRepository.Invocations);

        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel1.Object, TestContext.Current.CancellationToken), Times.Once);
        jobSource.Verify(s => s.HeartbeatAsync(rawJobModel2.Object, TestContext.Current.CancellationToken), Times.Once);
    }
}