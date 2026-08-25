using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs.Polling;

public class LoaderModeJobLoaderTests
{
    private static Mock<IExecutionEndArbiter> CreateExecutionEndArbiter()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()));
        return executionEndArbiter;
    }

    private static ICoreConfigurationService CreateCoreConfigurationService(
        bool haltOnFailure = false,
        bool treatTransientExceptionAsFailure = false)
    {
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.SetupGet(c => c.IsHaltOnFailure).Returns(haltOnFailure);
        coreConfiguration.SetupGet(c => c.IsTreatingTransientExceptionAsFailure)
            .Returns(treatTransientExceptionAsFailure);
        return coreConfiguration.Object;
    }

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
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 3}));

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
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 5}));

        await Assert.ThrowsAsync<BacklogFullException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        jobSource.Verify(s => s.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenDemandWaitIsCancelledByExecutionEnd_ThrowsAbortJobLoaderLoopException()
    {
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        Action<Exception?>? onStop = null;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()))
            .Callback<Action<Exception?>>(callback => onStop = callback);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            executionEndArbiter.Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        var runTask = loader.RunAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(onStop);
        onStop(null);

        await Assert.ThrowsAsync<AbortJobLoaderLoopException>(() => runTask);

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
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 2}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobSource.Verify(s => s.GetJobsAsync(2, TestContext.Current.CancellationToken), Times.Once);
        jobRepository.Verify(
            r => r.WaitForJobDemandAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenNoBacklogAndWatchedJobs_WaitsForDemandThenSubmits()
    {
        var response = CreateJobSourceResponse([new Mock<IRawJobModel>(MockBehavior.Strict).Object]);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(0);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(2);
        jobRepository
            .Setup(r => r.WaitForJobDemandAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 3}));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobRepository.Verify(r => r.WaitForJobDemandAsync(It.IsAny<CancellationToken>()), Times.Once);
        jobIntakeService.Verify(s => s.SubmitAsync(response, TestContext.Current.CancellationToken), Times.Once);
        jobSource.Verify(s => s.GetJobsAsync(3, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentWorkerJobSourceException_Propagates()
    {
        var permanent = new WorkerJobSourceException("unknown topic")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

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
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(true),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 2}));

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
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            CreateHealthStateUpdateService(),
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        await Assert.ThrowsAsync<NoJobException>(() => loader.RunAsync(TestContext.Current.CancellationToken));

        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task
        RunAsync_WhenTransientWorkerJobSourceException_AndTreatTransientAsFailure_AndHaltOnFailure_Propagates()
    {
        var transient = new WorkerJobSourceException("transient pull")
            {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true};

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(transient);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            health.Object,
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(true, true),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(transient, thrown);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        health.Verify(h => h.NoteIncident(), Times.Once);
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
            .ThrowsAsync(new WorkerJobSourceException("transient pull")
                {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true});

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            jobIntakeService.Object,
            health.Object,
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object,
            health.Object,
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        await Assert.ThrowsAsync<NoJobException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        health.Verify(h => h.NoteIncident(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenUnexpectedExceptionFromSource_Propagates()
    {
        var unexpected = new InvalidOperationException("auth failed");

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository.Setup(r => r.GetBacklogMaxCount()).Returns(1);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0);

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.GetJobsAsync(1, TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var loader = new LoaderModeJobLoader(
            jobSource.Object,
            CreateExecutionEndArbiter().Object,
            jobRepository.Object,
            new Mock<IJobIntakeService>(MockBehavior.Strict).Object,
            health.Object,
            new NullLogger<LoaderModeJobLoader>(),
            CreateCoreConfigurationService(true),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loader.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }
}