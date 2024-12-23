using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests;

public class HandlerTests
{
    [Fact]
    public async Task Test_Handler()
    {
        var jobManager = new Mock<IJobManager>();
        var loop = new Mock<IWorkerLoop>();

        var handler = new Handler(jobManager.Object, loop.Object);

        var cts = new CancellationTokenSource();

        await handler.HandleAsync(cts.Token);
        Assert.Single(loop.Invocations);
        jobManager.Verify(i => i.Start(cts.Token), Times.Once);
        loop.Verify(i => i.RunAsync(cts.Token), Times.Once);
    }
}