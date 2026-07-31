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

    private static BatchModeJobLoader CreateLoader(
        IExecutionEndArbiter executionEndArbiter,
        IJobLoaderStateService jobLoaderStateService,
        IJobRepository jobRepository,
        IJobSource jobSource,
        int batchSize = 10,
        int maxIdleWaitSeconds = 1,
        ISleepService? sleepService = null)
    {
        return new BatchModeJobLoader(
            executionEndArbiter,
            jobLoaderStateService,
            jobRepository,
            jobSource,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel
            {
                BatchSize = batchSize
            }),
            Options.Create(new LoopOptionsConfigurationModel
            {
                MaxIdleWaitSeconds = maxIdleWaitSeconds
            }),
            sleepService ?? CreateSleepService().Object);
    }

    [Fact]
    public async Task DoesNotStartProcessingWhenAlreadyStopping()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task EmptySourceExitsCleanlyWhenExecutionEnds()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        var keepRunning = true;
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var getJobsCount = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                getJobsCount++;
                if (getJobsCount >= 2)
                {
                    // On the retry predicate after the next NoJobException, stop handling/retrying.
                    keepRunning = false;
                }

                return new JobSourceResponse
                {
                    Items = []
                };
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        var sleepService = CreateSleepService();

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object,
            sleepService: sleepService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(getJobsCount >= 2);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.AtLeastOnce);
        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var response = new JobSourceResponse
        {
            Items = [new Mock<IJobModel>().Object]
        };

        var getJobsCount = 0;
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                getJobsCount++;
                if (getJobsCount < 3)
                {
                    return new JobSourceResponse
                    {
                        Items = []
                    };
                }

                // Stop the outer loop after this successful batch finishes.
                arbiterInvocationsRemaining = 0;
                return response;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var sleepService = CreateSleepService();

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object,
            sleepService: sleepService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, getJobsCount);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.Exactly(2));
        jobRepository.Verify(r => r.LoadAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
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

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var response = new JobSourceResponse
        {
            Items =
            [
                new Mock<IJobModel>().Object
            ]
        };

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(expectedBatchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object,
            configuredBatchSize);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(expectedBatchSize, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(r => r.LoadAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
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

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var firstBatch = new JobSourceResponse
        {
            Items = [new Mock<IJobModel>().Object]
        };
        var secondBatch = new JobSourceResponse
        {
            Items =
            [
                new Mock<IJobModel>().Object,
                new Mock<IJobModel>().Object
            ]
        };

        var responses = new Queue<JobSourceResponse>([firstBatch, secondBatch]);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => responses.Dequeue());

        var loadOrder = new List<JobSourceResponse>();
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Callback<JobSourceResponse, CancellationToken>((response, _) => loadOrder.Add(response))
            .Returns(Task.CompletedTask);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal([firstBatch, secondBatch], loadOrder);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken),
            Times.Exactly(2));
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task WaitsForEmptyRepositoryBeforeFetchingNextBatch()
    {
        var phase = 0;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => phase < 2);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var firstBatch = new JobSourceResponse
        {
            Items = [new Mock<IJobModel>().Object]
        };
        var secondBatch = new JobSourceResponse
        {
            Items = [new Mock<IJobModel>().Object]
        };

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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), TestContext.Current.CancellationToken))
            .Callback(() => callLog.Add("Load"))
            .Returns(Task.CompletedTask);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Callback(() =>
            {
                callLog.Add("WaitForEmpty");
                phase++;
            })
            .Returns(Task.CompletedTask);

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["GetJobs", "Load", "WaitForEmpty", "GetJobs", "Load", "WaitForEmpty"],
            callLog);
    }

    [Fact]
    public async Task RunAsync_ReturnsFinished()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobSource>(MockBehavior.Strict).Object);

        var result = await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
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
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .Returns<int, CancellationToken>((_, _) =>
            {
                getJobsCount++;
                if (getJobsCount == 1)
                {
                    throw new WorkerJobSourceException("transient pull", isCritical: false, couldBeTransient: true);
                }

                keepRunning = false;
                return Task.FromResult(new JobSourceResponse { Items = [] });
            });

        var sleepService = CreateSleepService();
        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            jobSource.Object,
            sleepService: sleepService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(getJobsCount >= 2);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), TestContext.Current.CancellationToken),
            Times.AtLeastOnce);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task CriticalWorkerJobSourceException_Propagates()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var critical = new WorkerJobSourceException("auth failed", isCritical: true, couldBeTransient: false);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(critical);

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            jobSource.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(critical, thrown);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task PermanentNonCriticalWorkerJobSourceException_Propagates()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var permanent = new WorkerJobSourceException("unknown topic", isCritical: false, couldBeTransient: false);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(permanent);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            jobRepository.Object,
            jobSource.Object);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IdleBackoff_IsCappedByMaxIdleWaitSeconds()
    {
        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>();
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(() => keepRunning);

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

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

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(new JobSourceResponse { Items = [] });

        var loader = CreateLoader(
            executionEndArbiter.Object,
            jobLoaderStateService.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            jobSource.Object,
            maxIdleWaitSeconds: 3,
            sleepService: sleepService.Object);

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(delays.Count >= 4);
        Assert.All(delays, delay => Assert.True(delay <= TimeSpan.FromSeconds(3)));
        Assert.Contains(TimeSpan.FromSeconds(2), delays);
        Assert.Contains(TimeSpan.FromSeconds(3), delays);
    }
}