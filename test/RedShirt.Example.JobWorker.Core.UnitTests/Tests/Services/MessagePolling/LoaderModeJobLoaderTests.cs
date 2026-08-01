using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Intake;
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

    private static LoaderModeJobLoader CreateLoader(
        IJobLoaderLoop jobLoaderLoop,
        IExecutionEndArbiter executionEndArbiter,
        IJobRepository jobRepository,
        IJobSource jobSource,
        IJobIntakeService jobIntakeService,
        int batchSize = 1)
    {
        return new LoaderModeJobLoader(
            jobLoaderLoop,
            jobSource,
            executionEndArbiter,
            jobRepository,
            jobIntakeService,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = batchSize}));
    }

    private static (Mock<IJobLoaderStateService> StateService, IJobLoaderLoop Loop) CreateJobLoaderLoop(
        IExecutionEndArbiter executionEndArbiter,
        Mock<ISleepService>? sleepService = null,
        int maxIdleWaitSeconds = 1)
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());
        var loop = TestJobHelpers.CreateJobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter,
            (sleepService ?? CreateSleepService()).Object,
            maxIdleWaitSeconds);
        return (jobLoaderStateService, loop);
    }

    [Fact]
    public async Task CriticalWorkerJobSourceException_Propagates()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var critical = new WorkerJobSourceException("auth failed");
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(critical);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object);

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

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var rawJob = TestJobHelpers.CreateRawJobModel();
        var response = TestJobHelpers.CreateJobSourceResponse(rawJob.Object);
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

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            jobIntakeService.Object,
            3);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, demandAttempts);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobSource.Verify(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task PermanentNonCriticalWorkerJobSourceException_Propagates()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var (_, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var permanent = new WorkerJobSourceException("unknown topic", false);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(2, TestContext.Current.CancellationToken))
            .ThrowsAsync(permanent);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(2);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            jobIntakeService.Object,
            2);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ReturnsFinished()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var (_, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobSource>(MockBehavior.Strict).Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object);

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

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(backlogSize);

        var response = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);
        var responses = new Queue<JobSourceResponse>();
        responses.Enqueue(response);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(expectedGetJobsBatchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => responses.TryDequeue(out var job)
                ? job
                : new JobSourceResponse {Items = []});

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            jobIntakeService.Object,
            configuredBatchSize);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 3)]
    public async Task TestLoadJobsWithFullBacklog(int backlogSize, int configuredBatchSize)
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task> callback, CancellationToken ct) =>
            {
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        await callback(ct);
                    }
                    catch (BacklogFullException)
                    {
                        // JobLoaderLoop retries ReasonToWaitException while the arbiter keeps running.
                    }
                }

                return HandlerResponseEnum.Finished;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(backlogSize);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(backlogSize);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(
            jobLoaderLoop.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            new Mock<IJobSource>(MockBehavior.Strict).Object,
            jobIntakeService.Object,
            configuredBatchSize);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public async Task TestLoadJobsWithNoBacklog(int configuredBatchSize, int expectedGetJobsBatchSize)
    {
        const int backlogSize = 0;

        var arbiterInvocationsRemaining = 5;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);
        jobRepository.Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(0);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(backlogSize);

        var response = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);
        var responses = new Queue<JobSourceResponse>();
        responses.Enqueue(response);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(expectedGetJobsBatchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => responses.TryDequeue(out var job)
                ? job
                : new JobSourceResponse {Items = []});

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            jobIntakeService.Object,
            configuredBatchSize);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact(Timeout = 1500)]
    public async Task TestLoadJobsWithNoBacklog_EmptyResult()
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

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken)).ReturnsAsync(1);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => false);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            new Mock<IJobSource>(MockBehavior.Strict).Object,
            jobIntakeService.Object,
            -1);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
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

        var sleepService = CreateSleepService();
        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object, sleepService);

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

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(
            jobLoaderLoop,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobSource.Object,
            jobIntakeService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(getJobsCount >= 2);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.AtLeastOnce);
        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }
}