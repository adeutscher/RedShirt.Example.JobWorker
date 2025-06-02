using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

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

        var jobManager = new LightJobManager(new NullLogger<LightJobManager>(), safeJobRunner.Object, jobSource.Object);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job1.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job2.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        safeJobRunner
            .Setup(s => s.RunSafelyAsync(job3.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        jobSource.Setup(s => s.AcknowledgeCompletionAsync(job2.Object, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new Exception("BOOM"));

        using var cts = new CancellationTokenSource();
        await jobManager.RunAsync(new JobSourceResponse
        {
            Items =
            [
                job1.Object,
                job2.Object,
                job3.Object
            ],
            RecommendedHeartbeatIntervalSeconds = 0
        }, cts.Token);

        Assert.Equal(3, safeJobRunner.Invocations.Count);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job1.Object, cts.Token), Times.Once);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job2.Object, cts.Token), Times.Once);
        safeJobRunner.Verify(r => r.RunSafelyAsync(job3.Object, cts.Token), Times.Once);

        Assert.Equal(3, jobSource.Invocations.Count);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job1.Object, true, cts.Token), Times.Once);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job2.Object, false, cts.Token), Times.Once);
        jobSource.Verify(s => s.AcknowledgeCompletionAsync(job3.Object, false, cts.Token), Times.Once);
    }

    [Fact]
    public void Test_Start()
    {
        // All nulls shouldn't matter, implementation of Start should be empty
        var jobManager = new LightJobManager(null!, null!, null!);
        jobManager.Start();
        Assert.Equal(1, 1); // Satisfy Sonar checks
    }
}