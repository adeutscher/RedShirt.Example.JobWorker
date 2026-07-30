using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class HandlerTests
{
    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 2, false)]
    public async Task TestRunAsync(int numberOfExecutorThreads, int expectedNumberOfThreads, bool wantMaintainer)
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(wantMaintainer ? 1 : 0);

        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var maintainer = new Mock<IMaintainer>(MockBehavior.Strict);
        if (wantMaintainer)
        {
            maintainer.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
                .Returns(Task.CompletedTask);
        }

        var options = new ThreadConfigurationModel
        {
            WorkerThreadCount = numberOfExecutorThreads
        };
        Assert.Equal(expectedNumberOfThreads, options.EffectiveWorkerThreadCount);

        var handler = new Handler(jobSource.Object, jobLoader.Object, maintainer.Object, executor.Object,
            Options.Create(options));

        await handler.HandleAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobLoader.Invocations);
        Assert.Equal(wantMaintainer ? 1 : 0, maintainer.Invocations.Count);
        Assert.Equal(expectedNumberOfThreads, executor.Invocations.Count);
        for (var i = 0; i < expectedNumberOfThreads; i++)
        {
            var i1 = i;
            executor.Verify(e => e.RunAsync(i1, TestContext.Current.CancellationToken));
        }
    }
}