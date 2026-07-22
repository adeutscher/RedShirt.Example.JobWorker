using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Loader;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Loader;

public class JobLoaderTests
{
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

        var loader = new JobLoader(jobLoaderStateService.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object,
            new NullLogger<JobLoader>(), Options.Create(loopOptions), Options.Create(jobSourceOptions));

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

        var loader = new JobLoader(jobLoaderStateService.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object,
            new NullLogger<JobLoader>(), Options.Create(loopOptions), Options.Create(jobSourceOptions));

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

        var loader = new JobLoader(jobLoaderStateService.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object,
            new NullLogger<JobLoader>(), Options.Create(loopOptions), Options.Create(jobSourceOptions));

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

        var loader = new JobLoader(jobLoaderStateService.Object, executionEndArbiter.Object, jobRepository.Object,
            jobSource.Object,
            new NullLogger<JobLoader>(), Options.Create(loopOptions), Options.Create(jobSourceOptions));

        await loader.RunAsync(TestContext.Current.CancellationToken);

        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);

        jobRepository.Verify(r => r.LoadAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
        jobRepository
            .Verify(r => r.WaitForJobDemandAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.AtLeast(2));
    }
}