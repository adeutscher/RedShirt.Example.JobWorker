using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class WorkerLoopTests
{
    [Fact]
    public async Task Test_RunManagerOnce()
    {
        var arbiterQueue = new Queue<string>();
        arbiterQueue.Enqueue("A");

        var endArbiter = new Mock<IExecutionEndArbiter>();
        endArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(() => arbiterQueue.TryDequeue(out _));
        var jobManager = new Mock<IJobManager>();
        var jobSource = new Mock<IJobSource>();

        var jobSourceResponse = new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 0,
            Items =
            [
                new Mock<IJobModel>().Object
            ]
        };
        jobSource.Setup(j => j.GetJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobSourceResponse);
        var loop = new WorkerLoop(endArbiter.Object, jobManager.Object, jobSource.Object, new NullLogger<WorkerLoop>(),
            Options.Create(new WorkerLoop.ConfigurationModel
            {
                MaxIdleWaitSeconds = 1
            }));

        var cts = new CancellationTokenSource();

        await loop.RunAsync(cts.Token);

        jobManager.Verify(j => j.Start(It.IsAny<CancellationToken>()), Times.Once);
        jobManager.Verify(j => j.RunAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()), Times.Once);
        jobManager.Verify(j => j.RunAsync(jobSourceResponse, cts.Token), Times.Once);

        jobSource.Verify(j => j.GetJobsAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task Test_RunTwice()
    {
        var arbiterQueue = new Queue<string>();
        arbiterQueue.Enqueue("A");
        arbiterQueue.Enqueue("B");

        var endArbiter = new Mock<IExecutionEndArbiter>();
        endArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(() => arbiterQueue.TryDequeue(out _));
        var jobManager = new Mock<IJobManager>();
        var jobSource = new Mock<IJobSource>();
        jobSource.Setup(j => j.GetJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobSourceResponse
            {
                RecommendedHeartbeatIntervalSeconds = 0,
                Items = []
            });
        var loop = new WorkerLoop(endArbiter.Object, jobManager.Object, jobSource.Object, new NullLogger<WorkerLoop>(),
            Options.Create(new WorkerLoop.ConfigurationModel
            {
                MaxIdleWaitSeconds = 1
            }));

        var cts = new CancellationTokenSource();

        await loop.RunAsync(cts.Token);

        jobManager.Verify(j => j.Start(It.IsAny<CancellationToken>()), Times.Once);
        jobManager.Verify(j => j.RunAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        jobSource.Verify(j => j.GetJobsAsync(cts.Token), Times.Exactly(2));
    }
}