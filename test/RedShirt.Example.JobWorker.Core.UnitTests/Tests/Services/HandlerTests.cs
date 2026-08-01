using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class HandlerTests
{
    [Fact]
    public async Task HandleAsync_DoesNotCompleteWhenOnlyNotEnabledWorkersFinish()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return HandlerResponseEnum.Finished;
            });

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var maintainer = new Mock<IMaintainer>(MockBehavior.Strict);
        maintainer.Setup(m => m.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        idempotencyMonitor.Setup(m => m.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var handler = new Handler(jobLoaderLoop.Object, maintainer.Object, executor.Object, idempotencyMonitor.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = 1}), new NullLogger<Handler>());

        var handleTask = handler.HandleAsync(cts.Token);

        // Give NotEnabled workers time to finish without unblocking HandleAsync.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(handleTask.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handleTask);
    }

    [Fact]
    public async Task HandleAsync_RethrowsUnhandledWorkerException()
    {
        var expected = new InvalidOperationException("worker blew up");

        // Only the failing worker may return Finished (via the catch path). Other workers
        // return NotEnabled so they cannot race ahead and clear the wait before the exception
        // is recorded.
        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(expected);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var maintainer = new Mock<IMaintainer>(MockBehavior.Strict);
        maintainer.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        idempotencyMonitor.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var handler = new Handler(jobLoaderLoop.Object, maintainer.Object, executor.Object, idempotencyMonitor.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = 1}), new NullLogger<Handler>());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(TestContext.Current.CancellationToken));

        Assert.Same(expected, thrown);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task TestRunAsync(int numberOfExecutorThreads, int expectedNumberOfThreads)
    {
        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.Finished);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.Finished);

        var maintainer = new Mock<IMaintainer>(MockBehavior.Strict);
        maintainer.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        idempotencyMonitor.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerResponseEnum.NotEnabled);

        var options = new ThreadConfigurationModel
        {
            WorkerThreadCount = numberOfExecutorThreads
        };
        Assert.Equal(expectedNumberOfThreads, options.EffectiveWorkerThreadCount);

        var handler = new Handler(jobLoaderLoop.Object, maintainer.Object, executor.Object, idempotencyMonitor.Object,
            Options.Create(options), new NullLogger<Handler>());

        await handler.HandleAsync(TestContext.Current.CancellationToken);

        Assert.Single(jobLoaderLoop.Invocations);
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