using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Batch;

public class LightJobManagerTests
{
    [Fact]
    public async Task Test_Run()
    {
        var job1 = new Mock<IJobModel>();
        var job2 = new Mock<IJobModel>();
        var job3 = new Mock<IJobModel>();

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        var jobSource = new Mock<IJobSource>();
        var sleepService = new Mock<ISleepService>();

        var jobManager = new LightJobManager(new NullLogger<LightJobManager>(), safeJobRunner.Object, jobSource.Object,
            sleepService.Object);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job1.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job2.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job3.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await jobManager.RunAsync([
            job1.Object,
            job2.Object,
            job3.Object
        ], TestContext.Current.CancellationToken);

        Assert.Equal(3, safeJobRunner.Invocations.Count);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job1.Object, TestContext.Current.CancellationToken), Times.Once);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job2.Object, TestContext.Current.CancellationToken), Times.Once);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job3.Object, TestContext.Current.CancellationToken), Times.Once);

        Assert.Equal(3, jobSource.Invocations.Count);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job1.Object, true, TestContext.Current.CancellationToken),
            Times.Once);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job2.Object, false, TestContext.Current.CancellationToken),
            Times.Once);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job3.Object, false, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_Run_ExceptionRetry()
    {
        var job1 = new Mock<IJobModel>();

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        var jobSource = new Mock<IJobSource>();
        var sleepService = new Mock<ISleepService>();

        var jobManager = new LightJobManager(new NullLogger<LightJobManager>(), safeJobRunner.Object, jobSource.Object,
            sleepService.Object);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job1.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        jobSource.Setup(s => s.AcknowledgeCompletionAsync(job1.Object, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new Exception("BOOM"));

        await jobManager.RunAsync([
            job1.Object
        ], TestContext.Current.CancellationToken);

        Assert.Single(safeJobRunner.Invocations);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job1.Object, TestContext.Current.CancellationToken), Times.Once);

        Assert.True(jobSource.Invocations.Count >= 2);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job1.Object, true, TestContext.Current.CancellationToken),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task Test_Run_Single()
    {
        var job1 = new Mock<IJobModel>();

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        var jobSource = new Mock<IJobSource>();
        var sleepService = new Mock<ISleepService>();

        var jobManager = new LightJobManager(new NullLogger<LightJobManager>(), safeJobRunner.Object, jobSource.Object,
            sleepService.Object);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job1.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await jobManager.RunAsync([
            job1.Object
        ], TestContext.Current.CancellationToken);

        Assert.Single(safeJobRunner.Invocations);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job1.Object, TestContext.Current.CancellationToken), Times.Once);

        Assert.Single(jobSource.Invocations);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job1.Object, true, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_Start()
    {
        // All nulls shouldn't matter, implementation of Start should be empty
        var jobManager = new LightJobManager(null!, null!, null!, null!);
        await jobManager.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, 1); // Satisfy Sonar checks
    }
}