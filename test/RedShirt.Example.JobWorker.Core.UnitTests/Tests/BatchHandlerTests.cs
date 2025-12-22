using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Batch;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests;

public class BatchHandlerTests
{
    [Fact]
    public async Task Test_Handler()
    {
        var jobManager = new Mock<IJobManager>();
        var loop = new Mock<IBatchWorkerLoop>();

        var handler = new BatchHandler(jobManager.Object, loop.Object);

        await handler.HandleAsync(TestContext.Current.CancellationToken);
        Assert.Single(loop.Invocations);
        jobManager.Verify(i => i.StartAsync(TestContext.Current.CancellationToken), Times.Once);
        loop.Verify(i => i.RunAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}