using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.MessagePolling;

public class LoaderModeJobLoaderTests
{
    private static IJobSourceResponse CreateJobSourceResponse(List<IRawJobModel> items)
    {
        var response = new Mock<IJobSourceResponse>(MockBehavior.Strict);
        response.Setup(r => r.Items).Returns(items);
        return response.Object;
    }

    [Fact]
    public async Task RunAsync_WhenBacklogHasCapacity_FetchesMinOfFreeSlotsAndBatchSizeThenSubmits()
    {
        var response = CreateJobSourceResponse([new Mock<IRawJobModel>(MockBehavior.Strict).Object]);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(4);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);

        // free slots = 4 - 1 = 3; batch size = 3 → request 3
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 3}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken), Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenBacklogIsFull_ThrowsBacklogFullException()
    {
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(2);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(2);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 5}));

        await Assert.ThrowsAsync<BacklogFullException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        jobSource.Verify(s => s.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenCriticalWorkerJobSourceException_Propagates()
    {
        var critical = new WorkerJobSourceException("auth failed");

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(critical);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(critical, thrown);
    }

    [Fact]
    public async Task RunAsync_WhenDemandWaitTimesOutAndStopping_ThrowsAbortJobLoaderLoopException()
    {
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ReturnsAsync(false);

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        await Assert.ThrowsAsync<AbortJobLoaderLoopException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        jobSource.Verify(s => s.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenNoBacklogAndNoWatchedJobs_FetchesEffectiveBatchSize()
    {
        var response = CreateJobSourceResponse([new Mock<IRawJobModel>(MockBehavior.Strict).Object]);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(2, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 2}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(2, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(
            r => r.WaitForJobDemandAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenNoBacklogAndWatchedJobs_WaitsForDemandThenSubmits()
    {
        var response = CreateJobSourceResponse([new Mock<IRawJobModel>(MockBehavior.Strict).Object]);

        var demandAttempts = 0;
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(2);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ReturnsAsync(() => ++demandAttempts >= 2);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 3}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, demandAttempts);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobSource.Verify(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentNonCriticalWorkerJobSourceException_Propagates()
    {
        var permanent = new WorkerJobSourceException("unknown topic", false);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(2);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(2, TestContext.Current.CancellationToken))
            .ThrowsAsync(permanent);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 2}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenSourceReturnsNoJobs_ThrowsNoJobException()
    {
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ReturnsAsync(CreateJobSourceResponse([]));

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenTransientWorkerJobSourceException_ThrowsNoJobException()
    {
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException("transient pull", false, true));

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            new Mock<IExecutionEndArbiter>(MockBehavior.Strict).Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<LoaderModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 1}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}