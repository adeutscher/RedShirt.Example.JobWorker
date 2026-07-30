using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class HandlerTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task TestRunAsync(int numberOfExecutorThreads, int expectedNumberOfThreads)
    {
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var maintainer = new Mock<IMaintainer>(MockBehavior.Strict);
        maintainer.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        idempotencyMonitor.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var options = new ThreadConfigurationModel
        {
            WorkerThreadCount = numberOfExecutorThreads
        };
        Assert.Equal(expectedNumberOfThreads, options.EffectiveWorkerThreadCount);

        var handler = new Handler(jobLoader.Object, maintainer.Object, executor.Object, idempotencyMonitor.Object,
            Options.Create(options));

        // Run
        await handler.HandleAsync(TestContext.Current.CancellationToken);

        // Check
        Assert.Single(jobLoader.Invocations);
        Assert.Equal(expectedNumberOfThreads, executor.Invocations.Count);
        for (var i = 0; i < expectedNumberOfThreads; i++)
        {
            var i1 = i;
            executor.Verify(e => e.RunAsync(i1, TestContext.Current.CancellationToken));
        }

        Assert.Single(maintainer.Invocations);
        Assert.Single(idempotencyMonitor.Invocations);
    }
}