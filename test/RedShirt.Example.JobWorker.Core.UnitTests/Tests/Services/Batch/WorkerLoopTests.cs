using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Batch;

public class BatchWorkerLoopTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Test_RunManagerOnce(int batchSize)
    {
        var arbiterQueue = new Queue<string>();
        arbiterQueue.Enqueue("A");

        var sourceOptions = new JobSourceConfigurationModel
        {
            BatchSize = batchSize
        };

        var endArbiter = new Mock<IExecutionEndArbiter>();
        endArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(() => arbiterQueue.TryDequeue(out _));
        var jobManager = new Mock<IJobManager>();
        var jobSource = new Mock<IJobSource>();

        var jobSourceResponse = new JobSourceResponse
        {
            Items =
            [
                new Mock<IJobModel>().Object
            ]
        };
        jobSource.Setup(j => j.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobSourceResponse);
        var loop = new BatchWorkerLoop(endArbiter.Object, jobManager.Object, jobSource.Object, new NullLogger<BatchWorkerLoop>(),
            Options.Create(sourceOptions), Options.Create(new BatchWorkerLoop.ConfigurationModel
            {
                MaxIdleWaitSeconds = 1
            }));

        await loop.RunAsync(TestContext.Current.CancellationToken);

        jobManager.Verify(j => j.RunAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()), Times.Once);
        jobManager.Verify(j => j.RunAsync(jobSourceResponse, TestContext.Current.CancellationToken), Times.Once);

        jobSource.Verify(j => j.GetJobsAsync(sourceOptions.EffectiveBatchSize, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_RunTwice()
    {
        var arbiterQueue = new Queue<string>();
        arbiterQueue.Enqueue("A");
        arbiterQueue.Enqueue("B");

        var sourceOptions = new JobSourceConfigurationModel
        {
            BatchSize = 5
        };

        var endArbiter = new Mock<IExecutionEndArbiter>();
        endArbiter.Setup(e => e.ShouldKeepRunning())
            .Returns(() => arbiterQueue.TryDequeue(out _));
        var jobManager = new Mock<IJobManager>();
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(j => j.GetJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobSourceResponse
            {
                Items = []
            });
        var loop = new BatchWorkerLoop(endArbiter.Object, jobManager.Object, jobSource.Object, new NullLogger<BatchWorkerLoop>(),
            Options.Create(sourceOptions), Options.Create(new BatchWorkerLoop.ConfigurationModel
            {
                MaxIdleWaitSeconds = 1
            }));

        await loop.RunAsync(TestContext.Current.CancellationToken);

        jobManager.Verify(j => j.RunAsync(It.IsAny<JobSourceResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        jobSource.Verify(j => j.GetJobsAsync(sourceOptions.BatchSize, TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }
}