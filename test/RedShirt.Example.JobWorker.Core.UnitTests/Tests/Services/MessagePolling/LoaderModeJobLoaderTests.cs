using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.MessagePolling;

public class LoaderModeJobLoaderTests
{
    private static Mock<ISleepService> CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService;
    }

    [Fact]
    public async Task CriticalWorkerJobSourceException_Propagates()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var critical = new WorkerJobSourceException("auth failed", true, false);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(critical);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);

        var loader = new LoaderModeJobLoader(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 1}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(critical, thrown);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task NoBacklog_WaitsForDemandThenLoadsJobs()
    {
        var arbiterInvocationsRemaining = 3;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var response = new JobSourceResponse {Items = [new Mock<IJobModel>().Object]};
        var demandAttempts = 0;

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository.Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(2);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                demandAttempts++;
                return demandAttempts >= 2;
            });
        jobRepository
            .Setup(r => r.LoadAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var loader = new LoaderModeJobLoader(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 1}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 3}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, demandAttempts);
        jobRepository.Verify(r => r.LoadAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobSource.Verify(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task PermanentNonCriticalWorkerJobSourceException_Propagates()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var permanent = new WorkerJobSourceException("unknown topic", false, false);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(2, TestContext.Current.CancellationToken))
            .ThrowsAsync(permanent);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(2);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);

        var loader = new LoaderModeJobLoader(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 1}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 2}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ReturnsFinished()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var loader = new LoaderModeJobLoader(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobSource>(MockBehavior.Strict).Object,
            CreateSleepService().Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 1}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        var result = await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(4, 3, 3)]
    public async Task TestLoadJobsWithBacklog(int backlogSize, int configuredBatchSize, int expectedGetJobsBatchSize)
    {
        var arbiterInvocationsRemaining = 5;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);
        jobRepository
            .Setup(r => r.GetBacklogMaxCount())
            .Returns(backlogSize);

        var response = new JobSourceResponse
        {
            Items =
            [
                new Mock<IJobModel>().Object
            ]
        };

        // ReSharper disable once CollectionNeverQueried.Local
        var responses = new Queue<JobSourceResponse>();
        responses.Enqueue(response);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(expectedGetJobsBatchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => responses.TryDequeue(out var job)
                ? job
                : new JobSourceResponse
                {
                    Items = []
                });

        var loopOptions = new LoopOptionsConfigurationModel
        {
            MaxIdleWaitSeconds = 1
        };

        var jobSourceOptions = new JobSourceConfigurationModel
        {
            BatchSize = configuredBatchSize
        };

        var loader = new LoaderModeJobLoader(jobLoaderStateService.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object, new NullLogger<LoaderModeJobLoader>(), Options.Create(loopOptions),
            Options.Create(jobSourceOptions));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);

        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Once);
        jobRepository.Verify(r => r.LoadAsync(response, TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Should never get to the point of loading jobs because the job repository is full.
    /// </summary>
    /// <param name="backlogSize"></param>
    /// <param name="configuredBatchSize"></param>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 3)]
    public async Task TestLoadJobsWithFullBacklog(int backlogSize, int configuredBatchSize)
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var arbiterInvocationsRemaining = 5;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(backlogSize);
        jobRepository
            .Setup(r => r.GetBacklogMaxCount())
            .Returns(backlogSize);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);

        var loopOptions = new LoopOptionsConfigurationModel
        {
            MaxIdleWaitSeconds = 1
        };

        var jobSourceOptions = new JobSourceConfigurationModel
        {
            BatchSize = configuredBatchSize
        };

        var loader = new LoaderModeJobLoader(jobLoaderStateService.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object, new NullLogger<LoaderModeJobLoader>(), Options.Create(loopOptions),
            Options.Create(jobSourceOptions));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);

        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public async Task TestLoadJobsWithNoBacklog(int configuredBatchSize, int expectedGetJobsBatchSize)
    {
        const int backlogSize = 0;

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var arbiterInvocationsRemaining = 5;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // No active jobs
        jobRepository
            .Setup(r => r.GetBacklogMaxCount())
            .Returns(backlogSize);

        var response = new JobSourceResponse
        {
            Items =
            [
                new Mock<IJobModel>().Object
            ]
        };

        // ReSharper disable once CollectionNeverQueried.Local
        var responses = new Queue<JobSourceResponse>();
        responses.Enqueue(response);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(expectedGetJobsBatchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => responses.TryDequeue(out var job)
                ? job
                : new JobSourceResponse
                {
                    Items = []
                });

        var loopOptions = new LoopOptionsConfigurationModel
        {
            MaxIdleWaitSeconds = 1
        };

        var jobSourceOptions = new JobSourceConfigurationModel
        {
            BatchSize = configuredBatchSize
        };

        var loader = new LoaderModeJobLoader(jobLoaderStateService.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object, new NullLogger<LoaderModeJobLoader>(), Options.Create(loopOptions),
            Options.Create(jobSourceOptions));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);

        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Once);
        jobRepository.Verify(r => r.LoadAsync(response, TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Verify that a job loader waiting for downstream demand will periodically check to confirm that the application
    ///     should still be running.
    /// </summary>
    [Fact(Timeout = 1500)]
    public async Task TestLoadJobsWithNoBacklog_EmptyResult()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var arbiterInvocationsRemaining = 5;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // 1 active job
        jobRepository
            .Setup(r => r.GetBacklogMaxCount())
            .Returns(0);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => false);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);

        var loopOptions = new LoopOptionsConfigurationModel
        {
            MaxIdleWaitSeconds = 1
        };

        var jobSourceOptions = new JobSourceConfigurationModel
        {
            BatchSize = -1
        };

        var loader = new LoaderModeJobLoader(jobLoaderStateService.Object, executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            CreateSleepService().Object, new NullLogger<LoaderModeJobLoader>(), Options.Create(loopOptions),
            Options.Create(jobSourceOptions));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);

        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository
            .Verify(r => r.WaitForJobDemandAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(2));
    }

    [Fact]
    public async Task TransientWorkerJobSourceException_IsTreatedAsNoJobsAndRetried()
    {
        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(() => keepRunning);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var getJobsCount = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .Returns<int, CancellationToken>((_, _) =>
            {
                getJobsCount++;
                if (getJobsCount == 1)
                {
                    throw new WorkerJobSourceException("transient pull", false, true);
                }

                keepRunning = false;
                return Task.FromResult(new JobSourceResponse {Items = []});
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);

        var sleepService = CreateSleepService();

        var loader = new LoaderModeJobLoader(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            sleepService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 1}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(getJobsCount >= 2);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.AtLeastOnce);
        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }
}