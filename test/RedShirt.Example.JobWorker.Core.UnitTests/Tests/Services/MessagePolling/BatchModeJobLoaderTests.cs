using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.MessagePolling;

public class BatchModeJobLoaderTests
{
    private static ICoreHealthStateUpdateService CreateHealthStateUpdateService()
    {
        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());
        return health.Object;
    }

    private static IJobSourceResponse CreateJobSourceResponse(List<IRawJobModel> items)
    {
        var response = new Mock<IJobSourceResponse>(MockBehavior.Strict);
        response.Setup(r => r.Items).Returns(items);
        return response.Object;
    }

    [Fact]
    public async Task RunAsync_UsesEffectiveBatchSizeWhenConfiguredBatchSizeIsBelowOne()
    {
        var response = CreateJobSourceResponse([new Mock<IRawJobModel>(MockBehavior.Strict).Object]);

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
            CreateHealthStateUpdateService(),
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 0}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenJobsReturned_SubmitsThenWaitsForEmptyRepository()
    {
        var response = CreateJobSourceResponse([new Mock<IRawJobModel>(MockBehavior.Strict).Object]);

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
            CreateHealthStateUpdateService(),
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["GetJobs", "Submit", "WaitForEmpty"], callLog);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentWorkerJobSourceException_Propagates()
    {
        var permanent = new WorkerJobSourceException("unknown topic")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

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
            CreateHealthStateUpdateService(),
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenSourceReturnsNoJobs_ThrowsNoJobException()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(5, TestContext.Current.CancellationToken))
            .ReturnsAsync(CreateJobSourceResponse([]));

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 5}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository.Verify(r => r.WaitForEmptyRepositoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenTransientWorkerJobSourceException_ThrowsNoJobException()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerJobSourceException("transient pull")
                {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true});

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            health.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenUnexpectedExceptionFromSource_AndHaltOnFailureFalse_ThrowsNoJobException()
    {
        var unexpected = new InvalidOperationException("auth failed");

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object,
            health.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        await Assert.ThrowsAsync<NoJobException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        health.Verify(h => h.NoteIncident(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenUnexpectedExceptionFromSource_Propagates()
    {
        var unexpected = new InvalidOperationException("auth failed");

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(10, TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new BatchModeJobLoader(
            jobSource.Object,
            new Mock<IJobRepository>(MockBehavior.Strict).Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object,
            health.Object,
            new NullLogger<BatchModeJobLoader>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            Options.Create(new JobSourceConfigurationModel {BatchSize = 10}));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }
}