using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Batch;

public class JobManagerTests
{
    /// <summary>
    ///     Confirm that the job acknowledgement should be allowed to fail without bringing everything else down.
    ///     Be careful about editing the timings on this because justification comments inside JobManager directly refer to
    ///     this test.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Test_RunJobAsync_Basic_Acknowledge_Failed()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return false;
            });
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s =>
                s.AcknowledgeCompletionAsync(It.IsAny<IJobModel>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel _, bool _, CancellationToken _) => throw new Exception("BOOM"));
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = [job.Object]
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
        jobSource.Verify(s => s.HeartbeatAsync(job.Object, It.IsAny<CancellationToken>()), Times.AtLeast(2));

        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job.Object, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    ///     Confirm that the heartbeat should be allowed to fail without bringing everything else down.
    ///     Be careful about editing the timings on this because justification comments inside JobManager directly refer to
    ///     this test.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Test_RunJobAsync_Basic_Heartbeat_Failed()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return false;
            });
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel _, CancellationToken _) => throw new Exception("BOOM"));
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = [job.Object]
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        jobSource.Verify(s => s.HeartbeatAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory(Timeout = 10000)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Test_RunJobAsync_Basic_Heartbeat_MultipleJobs(int numberOfJobs)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return numberOfJobs % 2 == 0;
            });
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);
        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 2
                }));

        var mocks = new List<Mock<IJobModel>>();
        var jobs = new List<IJobModel>();
        for (var i = 0; i < numberOfJobs; i++)
        {
            var item = new Mock<IJobModel>(MockBehavior.Strict);
            mocks.Add(item);
            jobs.Add(item.Object);
        }

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = jobs
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()),
            Times.Exactly(numberOfJobs));

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2 * numberOfJobs));
        for (var i = 0; i < numberOfJobs; i++)
        {
            var job = mocks[i]; // shorthand
            safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
            jobSource.Verify(s => s.HeartbeatAsync(job.Object, It.IsAny<CancellationToken>()), Times.AtLeast(2));
        }
    }

    /// <summary>
    ///     Test refreshes with one job.
    ///     Be careful about editing the timings on this because justification comments inside JobManager directly refer to
    ///     this test.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Test_RunJobAsync_Basic_Heartbeat_OneJob()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return false;
            });
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = [job.Object]
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
        jobSource.Verify(s => s.HeartbeatAsync(job.Object, It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    /// <summary>
    ///     Test refreshes with one job.
    ///     Be careful about editing the timings on this because justification comments inside JobManager directly refer to
    ///     this test.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Test_RunJobAsync_Basic_Heartbeat_OneJob_Instant()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel _, CancellationToken _) => Task.FromResult(true));
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(1);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = [job.Object]
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
        jobSource.Verify(s => s.HeartbeatAsync(job.Object, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    ///     Test refreshes with one job.
    ///     Be careful about editing the timings on this because justification comments inside JobManager directly refer to
    ///     this test.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Test_RunJobAsync_Basic_Heartbeat_OneJob_Long()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return false;
            });
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(10);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = [job.Object]
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Test_RunJobAsync_Basic_NoHeartbeat_MultipleJobs(int numberOfJobs)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(0);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var mocks = new List<Mock<IJobModel>>();
        var jobs = new List<IJobModel>();
        for (var i = 0; i < numberOfJobs; i++)
        {
            var item = new Mock<IJobModel>(MockBehavior.Strict);
            mocks.Add(item);
            jobs.Add(item.Object);
        }

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = jobs
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()),
            Times.Exactly(numberOfJobs));
        for (var i = 0; i < numberOfJobs; i++)
        {
            var item = mocks[i].Object;
            safeRunner.Verify(s => s.RunSafelyAsync(item, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task Test_RunJobAsync_Basic_NoHeartbeat_OneJob()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(true);

        var safeRunner = new Mock<ISafeJobRunner>();
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(s => s.RecommendedHeartbeatIntervalSeconds).Returns(0);

        var jobManager = new JobManager(executionEndArbiter.Object, safeRunner.Object, jobSource.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new ThreadConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items = [job.Object]
        }, TestContext.Current.CancellationToken);

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }
}