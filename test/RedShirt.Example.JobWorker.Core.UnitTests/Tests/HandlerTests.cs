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

        await handler.HandleAsync(TestContext.Current.CancellationToken);
        Assert.Single(loop.Invocations);
        jobManager.Verify(i => i.StartAsync(TestContext.Current.CancellationToken), Times.Once);
        loop.Verify(i => i.RunAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}