using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Intake;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.MessagePolling;

public class BatchModeJobLoaderTests
{
    private static Mock<ISleepService> CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService;
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

    private static BatchModeJobLoader CreateLoader(
        IJobLoaderLoop jobLoaderLoop,
        IJobRepository jobRepository,
        IJobSource jobSource,
        IJobIntakeService jobIntakeService,
        int batchSize = 10)
    {
        return new BatchModeJobLoader(
            jobSource,
            jobRepository,
            jobIntakeService,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = batchSize}),
            jobLoaderLoop);
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
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(critical);

        var loader = CreateLoader(
            jobLoaderLoop,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            jobSource.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(critical, thrown);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task DoesNotStartProcessingWhenAlreadyStopping()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(jobLoaderLoop, jobRepository.Object, jobSource.Object, jobIntakeService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task EmptySourceExitsCleanlyWhenExecutionEnds()
    {
        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(() => keepRunning);

        var sleepService = CreateSleepService();
        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object, sleepService);

        var getJobsCount = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                getJobsCount++;
                if (getJobsCount >= 2)
                {
                    keepRunning = false;
                }

                return new JobSourceResponse {Items = []};
            });

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(
            jobLoaderLoop,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
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

    [Fact]
    public async Task EmptySourceRetriesUntilJobsArrive()
    {
        var arbiterInvocationsRemaining = 10;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining >= 0;
            });

        var sleepService = CreateSleepService();
        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object, sleepService);

        var response = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);

        var getJobsCount = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                getJobsCount++;
                if (getJobsCount < 3)
                {
                    return new JobSourceResponse {Items = []};
                }

                arbiterInvocationsRemaining = 0;
                return response;
            });

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(jobLoaderLoop, jobRepository.Object, jobSource.Object, jobIntakeService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, getJobsCount);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.Exactly(2));
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task IdleBackoff_IsCappedByMaxIdleWaitSeconds()
    {
        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(() => keepRunning);

        var delays = new List<TimeSpan>();
        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((delay, _) =>
            {
                delays.Add(delay);
                if (delays.Count >= 4)
                {
                    keepRunning = false;
                }

                return Task.CompletedTask;
            });

        var (_, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object, sleepService, 3);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(new JobSourceResponse {Items = []});

        var loader = CreateLoader(
            jobLoaderLoop,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            jobSource.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(delays.Count >= 4);
        Assert.All(delays, delay => Assert.True(delay <= TimeSpan.FromSeconds(3)));
        Assert.Contains(TimeSpan.FromSeconds(2), delays);
        Assert.Contains(TimeSpan.FromSeconds(3), delays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task LoadsJobsThenWaitsForEmptyRepository(int configuredBatchSize)
    {
        var expectedBatchSize = Math.Max(configuredBatchSize, 1);

        var arbiterInvocationsRemaining = 2;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() =>
            {
                arbiterInvocationsRemaining--;
                return arbiterInvocationsRemaining > 0;
            });

        var (jobLoaderStateService, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var response = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(expectedBatchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(
            jobLoaderLoop,
            jobRepository.Object,
            jobSource.Object,
            jobIntakeService.Object,
            configuredBatchSize);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(expectedBatchSize, TestContext.Current.CancellationToken), Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
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
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(permanent);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = CreateLoader(jobLoaderLoop, jobRepository.Object, jobSource.Object, jobIntakeService.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobIntakeService.Verify(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessesMultipleBatchesSequentially()
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

        var firstBatch = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);
        var secondBatch = TestJobHelpers.CreateJobSourceResponse(
            TestJobHelpers.CreateRawJobModel().Object,
            TestJobHelpers.CreateRawJobModel().Object);

        var responses = new Queue<JobSourceResponse>([firstBatch, secondBatch]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => responses.Dequeue());

        var submitOrder = new List<JobSourceResponse>();
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Callback<JobSourceResponse, CancellationToken>((response, _) => submitOrder.Add(response))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(jobLoaderLoop, jobRepository.Object, jobSource.Object, jobIntakeService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal([firstBatch, secondBatch], submitOrder);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken),
            Times.Exactly(2));
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ReturnsFinished()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var (_, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var loader = CreateLoader(
            jobLoaderLoop,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobSource>(MockBehavior.Strict).Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object);

        var result = await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
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
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
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

        var loader = CreateLoader(
            jobLoaderLoop,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            jobSource.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(getJobsCount >= 2);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.AtLeastOnce);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task WaitsForEmptyRepositoryBeforeFetchingNextBatch()
    {
        var phase = 0;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(() => phase < 2);

        var (_, jobLoaderLoop) = CreateJobLoaderLoop(executionEndArbiter.Object);

        var firstBatch = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);
        var secondBatch = TestJobHelpers.CreateJobSourceResponse(TestJobHelpers.CreateRawJobModel().Object);
        var responses = new Queue<JobSourceResponse>([firstBatch, secondBatch]);
        var callLog = new List<string>();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                callLog.Add("GetJobs");
                return responses.Dequeue();
            });

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Callback(() => callLog.Add("Submit"))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Callback(() =>
            {
                callLog.Add("WaitForEmpty");
                phase++;
            })
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(jobLoaderLoop, jobRepository.Object, jobSource.Object, jobIntakeService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["GetJobs", "Submit", "WaitForEmpty", "GetJobs", "Submit", "WaitForEmpty"],
            callLog);
    }
}