using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Models.Loader;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Loader;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Loader;

public class JobRepositoryTests
{
    [Fact(Timeout = 500)]
    public async Task TestGetAllInFlightJobsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        var expectedJobs = new List<Mock<IJobModel>>();

        for (var i = 0; i < 3; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.Inactive);
            var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
            jobModel.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());
            expectedJobs.Add(jobModel);
            job.Setup(j => j.JobModel).Returns(jobModel.Object);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 2; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.Active);
            var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
            jobModel.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());
            expectedJobs.Add(jobModel);
            job.Setup(j => j.JobModel).Returns(jobModel.Object);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 1; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.Complete);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        var jobs = await jobRepository.GetAllInFlightJobsAsync(TestContext.Current.CancellationToken);
        Assert.NotSame(expectedJobs, jobRepository.WatchedJobs);
        Assert.Equal(5, jobs.Count); // Excludes the Complete one

        foreach (var t in expectedJobs)
        {
            Assert.Single(jobs, job => job.JobModel.MessageId == t.Object.MessageId);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)] // Confirm use of Math.Max
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void TestGetBacklogMaxCount(int backlogSize, int expectedEffectiveBatchSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = backlogSize
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        Assert.Equal(expectedEffectiveBatchSize, jobRepository.GetBacklogMaxCount());
    }

    [Fact(Timeout = 500)]
    public async Task TestGetCountsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        Mock<IJobRepositoryEntry> job;

        for (var i = 0; i < 3; i++)
        {
            job = new Mock<IJobRepositoryEntry>();
            job.Setup(j => j.State).Returns(JobState.Inactive);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 2; i++)
        {
            job = new Mock<IJobRepositoryEntry>();
            job.Setup(j => j.State).Returns(JobState.Active);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 1; i++)
        {
            job = new Mock<IJobRepositoryEntry>();
            job.Setup(j => j.State).Returns(JobState.Complete);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        Assert.Equal(3, await jobRepository.GetInactiveJobCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(6, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     If the internal jobs queue is empty and the execution end arbiter says there's no continuing,
    ///     then return null for GetNextJobAsync.
    /// </summary>
    [Fact(Timeout = 500)]
    public async Task TestGetNextJobAsync_Null()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        Assert.Null(await jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken));
    }

    [Theory(Timeout = 500)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TestLoadJobs(int responseSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        var response = new JobSourceResponse
        {
            Items = []
        };

        var items = new List<Mock<IJobModel>>();

        for (var i = 0; i < responseSize; i++)
        {
            var currentItem = new Mock<IJobModel>(MockBehavior.Strict);
            currentItem.Setup(ci => ci.MessageId).Returns(Guid.NewGuid().ToString());
            items.Add(currentItem);
            response.Items.Add(currentItem.Object);
        }

        await jobRepository.LoadAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(responseSize, jobRepository.WatchedJobs.Count);
        for (var i = 0; i < responseSize; i++)
        {
            var currentMock = items[i];
            var job = Assert.Single(jobRepository.WatchedJobs,
                ci => ci.JobModel.MessageId == currentMock.Object.MessageId);
            Assert.InRange(job.LastHeartbeatTime,
                DateTime.UtcNow - TimeSpan.FromMilliseconds(250),
                DateTime.UtcNow + TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>
    ///     Confirm that LoadJobs sends at least one JobsArrived event that can be picked up by.
    ///     Expansion of TestLoadJobs
    /// </summary>
    /// <param name="responseSize"></param>
    [Theory(Timeout = 1000)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TestLoadJobsAndWaitForJob(int responseSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        var response = new JobSourceResponse
        {
            Items = []
        };

        var items = new List<Mock<IJobModel>>();

        for (var i = 0; i < responseSize; i++)
        {
            var currentItem = new Mock<IJobModel>(MockBehavior.Strict);
            currentItem.Setup(ci => ci.MessageId).Returns(Guid.NewGuid().ToString());
            items.Add(currentItem);
            response.Items.Add(currentItem.Object);
        }

        // Notably doing this BEFORE loading in jobs
        // Also intentionally not awaiting it just yet
        var getJobTask = Task.Run(() => jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken));

        await jobRepository.LoadAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(responseSize, jobRepository.WatchedJobs.Count);
        for (var i = 0; i < responseSize; i++)
        {
            var currentMock = items[i];
            var job = Assert.Single(jobRepository.WatchedJobs,
                ci => ci.JobModel.MessageId == currentMock.Object.MessageId);
            Assert.InRange(job.LastHeartbeatTime,
                DateTime.UtcNow - TimeSpan.FromMilliseconds(250),
                DateTime.UtcNow + TimeSpan.FromMilliseconds(250));
        }

        var gottenJob = await getJobTask;
        Assert.NotNull(gottenJob);
        // Matches at least one
        Assert.Contains(items, i => i.Object.MessageId == gottenJob.JobModel.MessageId);
    }

    [Fact(Timeout = 500)]
    public async Task TestRemoveJobsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        var job = new Mock<IJobRepositoryEntry>();
        job.Setup(j => j.State).Returns(JobState.Complete);
        jobRepository.WatchedJobs.Add(job.Object);

        Assert.Equal(0, await jobRepository.GetInactiveJobCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));

        await jobRepository.RemoveJobAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(0, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 500)]
    public async Task TestRemoveJobsAsyncB()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        var job = new Mock<IJobRepositoryEntry>();
        job.Setup(j => j.State).Returns(JobState.Complete);
        jobRepository.WatchedJobs.Add(job.Object);

        var job2 = new Mock<IJobRepositoryEntry>();
        job2.Setup(j => j.State).Returns(JobState.Complete);
        jobRepository.WatchedJobs.Add(job2.Object);

        Assert.Equal(0, await jobRepository.GetInactiveJobCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));

        await jobRepository.RemoveJobAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(1, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestWaitForDemand()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var jobRepository = new JobRepository(executionEndArbiter.Object, new NullLogger<JobRepository>(),
            Options.Create(options));

        // Start waiting for there to be a job demand
        var demandTask = Task.Run(() => jobRepository.WaitForJobDemandAsync(TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        // Demand a job. We don't care about this one finishing
        var _ = Task.Run(() => jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken));

        await demandTask;
        Assert.True(true); // Satisfy Sonar. The true assert is this test not timing out.
    }
}