using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class JobRepositoryTests
{
    [Fact(Timeout = 500)]
    public async Task TestGetAllInFlightJobsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
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

        for (var i = 0; i < 2; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.BlockedByIdempotency);
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
        Assert.Equal(7, jobs.Count); // Excludes the Complete one; includes BlockedByIdempotency

        foreach (var t in expectedJobs)
        {
            Assert.Single(jobs, job => job.JobModel.MessageId == t.Object.MessageId);
        }
    }

    [Fact(Timeout = 500)]
    public async Task TestGetAllIdempotencyBlockedJobsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var expectedBlockedJobs = new List<Mock<IJobModel>>();

        for (var i = 0; i < 2; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.Inactive);
            var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
            jobModel.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());
            job.Setup(j => j.JobModel).Returns(jobModel.Object);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 1; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.Active);
            var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
            jobModel.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());
            job.Setup(j => j.JobModel).Returns(jobModel.Object);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 3; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.BlockedByIdempotency);
            var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
            jobModel.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());
            expectedBlockedJobs.Add(jobModel);
            job.Setup(j => j.JobModel).Returns(jobModel.Object);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        for (var i = 0; i < 1; i++)
        {
            var job = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
            job.Setup(j => j.State).Returns(JobState.Complete);
            jobRepository.WatchedJobs.Add(job.Object);
        }

        var jobs = await jobRepository.GetAllIdempotencyBlockedJobsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, jobs.Count);

        foreach (var t in expectedBlockedJobs)
        {
            Assert.Single(jobs, job => job.JobModel.MessageId == t.Object.MessageId);
        }

        Assert.All(jobs, job => Assert.Equal(JobState.BlockedByIdempotency, job.State));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)] // Confirm use of Math.Max
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void TestGetBacklogMaxCount(int backlogSize, int expectedEffectiveBatchSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = backlogSize
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        Assert.Equal(expectedEffectiveBatchSize, jobRepository.GetBacklogMaxCount());
    }

    [Fact(Timeout = 500)]
    public async Task TestGetCountsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object, sorter.Object,
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

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService
            .Setup(s => s.IsLoaderFinished())
            .Returns(true);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        Assert.Null(await jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken));

        jobLoaderStateService.Verify(s => s.IsLoaderFinished(), Times.Once);
    }

    [Theory(Timeout = 2000)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TestLoadJobs(int responseSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
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
            Assert.True(job.FlightTimeCanBeExtended);
            Assert.InRange(job.LastHeartbeatTime,
                DateTime.UtcNow - TimeSpan.FromMilliseconds(250),
                DateTime.UtcNow + TimeSpan.FromMilliseconds(250));
        }

        sorter.Verify(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()), Times.AtLeastOnce);
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

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var response = new JobSourceResponse
        {
            Items = []
        };

        var mockJobs = new List<Mock<IJobModel>>();

        for (var i = 0; i < responseSize; i++)
        {
            var currentItem = new Mock<IJobModel>(MockBehavior.Strict);
            currentItem.Setup(ci => ci.MessageId).Returns(Guid.NewGuid().ToString());
            mockJobs.Add(currentItem);
            response.Items.Add(currentItem.Object);
        }

        // Notably doing this BEFORE loading in jobs
        // Also intentionally not awaiting it just yet
        var getJobTask = Task.Run(() => jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken));

        await jobRepository.LoadAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(responseSize, jobRepository.WatchedJobs.Count);
        for (var i = 0; i < responseSize; i++)
        {
            var currentMock = mockJobs[i];
            var job = Assert.Single(jobRepository.WatchedJobs,
                ci => ci.JobModel.MessageId == currentMock.Object.MessageId);
            Assert.True(job.FlightTimeCanBeExtended);
            Assert.InRange(job.LastHeartbeatTime,
                DateTime.UtcNow - TimeSpan.FromMilliseconds(250),
                DateTime.UtcNow + TimeSpan.FromMilliseconds(250));
        }

        var gottenJob = await getJobTask;
        Assert.NotNull(gottenJob);
        // Matches at least one
        Assert.Contains(mockJobs, i => i.Object.MessageId == gottenJob.JobModel.MessageId);
    }

    /// <summary>
    ///     Verify that receiving jobs from the repository actually removes said jobs from consideration for follow-up.
    ///     Made in response to verify fix of a logic problem advised by Cursor
    /// </summary>
    /// <param name="responseSize"></param>
    [Theory(Timeout = 1000)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TestLoadJobsAndWaitForJob_UntilEmpty(int responseSize)
    {
        var readyToEnd = false;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            // ReSharper disable once AccessToModifiedClosure
            .Returns(() => !readyToEnd);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService
            .Setup(s => s.IsLoaderFinished())
            .Returns(true);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var response = new JobSourceResponse
        {
            Items = []
        };

        var items = new List<Mock<IJobModel>>();
        var jobIdentifiersTracked = new HashSet<string>();

        for (var i = 0; i < responseSize; i++)
        {
            var currentItem = new Mock<IJobModel>(MockBehavior.Strict);
            var currentId = Guid.NewGuid().ToString();
            currentItem.Setup(ci => ci.MessageId).Returns(currentId);
            jobIdentifiersTracked.Add(currentId);
            items.Add(currentItem);
            response.Items.Add(currentItem.Object);
        }

        // Compile a list of retrieved jobs
        // Notably doing this BEFORE loading in jobs
        // Also intentionally not awaiting it just yet
        var retrievedJobsTask = Task.Run(async () =>
        {
            var jobs = new List<IJobRepositoryEntry>();

            IJobRepositoryEntry? currentJob;
            do
            {
                currentJob = await jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken);
                if (currentJob is not null)
                {
                    jobs.Add(currentJob);
                }
            } while (currentJob is not null);

            return jobs;
        });

        await jobRepository.LoadAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(responseSize, jobRepository.WatchedJobs.Count);
        for (var i = 0; i < responseSize; i++)
        {
            var currentMock = items[i];
            var job = Assert.Single(jobRepository.WatchedJobs,
                ci => ci.JobModel.MessageId == currentMock.Object.MessageId);
            Assert.True(job.FlightTimeCanBeExtended);
            Assert.InRange(job.LastHeartbeatTime,
                DateTime.UtcNow - TimeSpan.FromMilliseconds(250),
                DateTime.UtcNow + TimeSpan.FromMilliseconds(250));
        }

        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        readyToEnd = true;
        var retrievedJobs = await retrievedJobsTask;
        Assert.NotNull(retrievedJobs);
        Assert.NotEmpty(retrievedJobs);

        var jobIdentifiersRetrieved = new HashSet<string>();

        for (var i = 0; i < responseSize; i++)
        {
            var currentJob = retrievedJobs[i]; // shorthand
            Assert.Contains(currentJob.JobModel.MessageId, jobIdentifiersTracked);
            Assert.True(jobIdentifiersRetrieved.Add(currentJob.JobModel.MessageId));
        }

        jobLoaderStateService.Verify(s => s.IsLoaderFinished(), Times.Once);
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
    public async Task TestLoadJobsAndWaitForJob_VerifySetToActive(int responseSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var response = new JobSourceResponse
        {
            Items = []
        };

        var mockJobs = new List<Mock<IJobModel>>();

        for (var i = 0; i < responseSize; i++)
        {
            var currentItem = new Mock<IJobModel>(MockBehavior.Strict);
            currentItem.Setup(ci => ci.MessageId).Returns(Guid.NewGuid().ToString());
            mockJobs.Add(currentItem);
            response.Items.Add(currentItem.Object);
        }

        await jobRepository.LoadAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(responseSize, jobRepository.WatchedJobs.Count);
        for (var i = 0; i < responseSize; i++)
        {
            var currentMock = mockJobs[i];
            var job = Assert.Single(jobRepository.WatchedJobs,
                ci => ci.JobModel.MessageId == currentMock.Object.MessageId);
            Assert.True(job.FlightTimeCanBeExtended);
            Assert.True(job.State == JobState.Inactive);
            Assert.InRange(job.LastHeartbeatTime,
                DateTime.UtcNow - TimeSpan.FromMilliseconds(250),
                DateTime.UtcNow + TimeSpan.FromMilliseconds(250));
        }

        // Unlike the mainline test, call GetNextJob afterwards (doing so because we wanted to be 100% sure that WatchedJobs was checked before retrieving any jobs)
        var gottenJob = await jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(gottenJob);
        // Matches at least one
        Assert.Contains(mockJobs, i => i.Object.MessageId == gottenJob.JobModel.MessageId);

        // After having grabbed a job, look at WatchedJobs again. One of them should have been flipped to Active
        Assert.Single(jobRepository.WatchedJobs, wj => wj.State == JobState.Active);
    }

    [Fact(Timeout = 500)]
    public async Task TestRemoveJobsAsync()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
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

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
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

    [Fact(Timeout = 1000)]
    public async Task TestWaitForDemand_False()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        // Start waiting for there to be a job demand
        var stopwatch = Stopwatch.StartNew();
        // The Task.Run wrapper is a bit silly for this particular test, but it keeps things more consistent with similar tests
        var demandTask = Task.Run(
            () => jobRepository.WaitForJobDemandAsync(TimeSpan.FromMilliseconds(250),
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        var demandResult = await demandTask;
        stopwatch.Stop();
        Assert.False(demandResult);

        // Confirm that things took a moment to run
        Assert.True(stopwatch.ElapsedMilliseconds > 150);
    }

    [Fact]
    public async Task TestWaitForDemand_True()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);

        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };

        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        // Start waiting for there to be a job demand
        var demandTask = Task.Run(
            () => jobRepository.WaitForJobDemandAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        // Demand a job. We don't care about this one finishing
        _ = Task.Run(() => jobRepository.GetNextJobAsync(TestContext.Current.CancellationToken));

        var demandResult = await demandTask;
        Assert.True(demandResult);
    }

    [Fact(Timeout = 2000)]
    public async Task WaitForEmptyRepositoryAsync_CompletesWhenLastJobRemoved()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };
        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var jobModel1 = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel1.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());
        var jobModel2 = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel2.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());

        await jobRepository.LoadAsync(new JobSourceResponse
        {
            Items = [jobModel1.Object, jobModel2.Object]
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));

        var waitTask = Task.Run(
            () => jobRepository.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        // Allow the waiter to observe a non-empty repository before draining it.
        await Task.Yield();
        Assert.False(waitTask.IsCompleted);

        var watched = jobRepository.WatchedJobs.ToList();
        Assert.Equal(2, watched.Count);

        await jobRepository.RemoveJobAsync(watched[0], TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);
        Assert.Equal(1, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));

        await jobRepository.RemoveJobAsync(watched[1], TestContext.Current.CancellationToken);
        await waitTask;

        Assert.Equal(0, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 2000)]
    public async Task WaitForEmptyRepositoryAsync_HonorsCancellation()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };
        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(m => m.MessageId).Returns(Guid.NewGuid().ToString());

        await jobRepository.LoadAsync(new JobSourceResponse
        {
            Items = [jobModel.Object]
        }, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        var waitTask = jobRepository.WaitForEmptyRepositoryAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
        Assert.Equal(1, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForEmptyRepositoryAsync_WhenAlreadyEmpty_ReturnsImmediately()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        var options = new JobRepository.ConfigurationModel
        {
            BacklogSize = 0
        };
        var sorter = new Mock<ISourceMessageSorter>();
        sorter
            .Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobRepositoryEntry>>()))
            .Returns((List<IJobRepositoryEntry> input) => input);

        var jobRepository = new JobRepository(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            sorter.Object,
            Options.Create(options));

        var stopwatch = Stopwatch.StartNew();
        await jobRepository.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(0, await jobRepository.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken));
        Assert.True(stopwatch.ElapsedMilliseconds < 250);
    }
}