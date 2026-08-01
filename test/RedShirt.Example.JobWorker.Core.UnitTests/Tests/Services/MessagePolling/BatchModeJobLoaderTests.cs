using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.MessagePolling;

public class BatchModeJobLoaderTests
{
    [Fact]
    public async Task RunAsync_UsesEffectiveBatchSizeWhenConfiguredBatchSizeIsBelowOne()
    {
        var response = new JobSourceResponse
        {
            Items = [new Mock<IRawJobModel>(MockBehavior.Strict).Object]
        };

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 0}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenCriticalWorkerJobSourceException_Propagates()
    {
        var critical = new WorkerJobSourceException("auth failed");

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(critical);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(critical, thrown);
    }

    [Fact]
    public async Task RunAsync_WhenJobsReturned_SubmitsThenWaitsForEmptyRepository()
    {
        var response = new JobSourceResponse
        {
            Items = [new Mock<IRawJobModel>(MockBehavior.Strict).Object]
        };

        var callLog = new List<string>();

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                callLog.Add("GetJobs");
                return response;
            });

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .Callback(() => callLog.Add("Submit"))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.WaitForEmptyRepositoryAsync(TestContext.Current.CancellationToken))
            .Callback(() => callLog.Add("WaitForEmpty"))
            .Returns(Task.CompletedTask);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["GetJobs", "Submit", "WaitForEmpty"], callLog);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentNonCriticalWorkerJobSourceException_Propagates()
    {
        var permanent = new WorkerJobSourceException("unknown topic", false);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(permanent);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenSourceReturnsNoJobs_ThrowsNoJobException()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(5, TestContext.Current.CancellationToken))
            .ReturnsAsync(new JobSourceResponse {Items = []});

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 5}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenTransientWorkerJobSourceException_ThrowsNoJobException()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException("transient pull", false, true));

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}