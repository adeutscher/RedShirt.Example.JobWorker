using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class JobManagerTests
{
    [Theory(Timeout = 10000)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Test_RunJobAsync_Basic_Heartbeat_MultipleJobs(int numberOfJobs)
    {
        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return numberOfJobs % 2 == 0;
            });
        var failureHandler = new Mock<IJobFailureHandler>();
        var jobSource = new Mock<IJobSource>();
        var jobManager = new JobManager(safeRunner.Object, jobSource.Object, failureHandler.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new JobManager.ConfigurationModel
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

        jobManager.Start();
        await jobManager.RunAsync(new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 1,
            Items = jobs
        });

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
        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return false;
            });
        var jobSource = new Mock<IJobSource>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var jobManager = new JobManager(safeRunner.Object, jobSource.Object, failureHandler.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new JobManager.ConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        jobManager.Start();
        await jobManager.RunAsync(new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 1,
            Items = [job.Object]
        });

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
    public async Task Test_RunJobAsync_Basic_Heartbeat_OneJob_Long()
    {
        var safeRunner = new Mock<ISafeJobRunner>();
        safeRunner
            .Setup(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel _, CancellationToken ct) =>
            {
                await Task.Delay(2500, ct);
                return false;
            });
        var jobSource = new Mock<IJobSource>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var jobManager = new JobManager(safeRunner.Object, jobSource.Object, failureHandler.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new JobManager.ConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        jobManager.Start();
        await jobManager.RunAsync(new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 10,
            Items = [job.Object]
        });

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);

        jobSource.Verify(s => s.HeartbeatAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Test_RunJobAsync_Basic_NoHeartbeat_MultipleJobs(int numberOfJobs)
    {
        var safeRunner = new Mock<ISafeJobRunner>();
        var jobSource = new Mock<IJobSource>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var jobManager = new JobManager(safeRunner.Object, jobSource.Object, failureHandler.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new JobManager.ConfigurationModel
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

        jobManager.Start();
        await jobManager.RunAsync(new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 0,
            Items = jobs
        });

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
        var safeRunner = new Mock<ISafeJobRunner>();
        var jobSource = new Mock<IJobSource>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var jobManager = new JobManager(safeRunner.Object, jobSource.Object, failureHandler.Object,
            new NullLogger<JobManager>(),
            Options.Create(
                new JobManager.ConfigurationModel
                {
                    WorkerThreadCount = 1
                }));

        var job = new Mock<IJobModel>(MockBehavior.Strict);

        jobManager.Start();
        await jobManager.RunAsync(new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 0,
            Items = [job.Object]
        });

        safeRunner.Verify(s => s.RunSafelyAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()), Times.Once);
        safeRunner.Verify(s => s.RunSafelyAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }
}