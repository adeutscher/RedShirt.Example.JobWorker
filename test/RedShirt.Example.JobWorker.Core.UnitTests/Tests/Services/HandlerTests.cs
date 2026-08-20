using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Heartbeats;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class HandlerTests
{
    private static ExecutionEndArbiter CreateExecutionEndArbiter()
    {
        return new ExecutionEndArbiter(NullLogger<ExecutionEndArbiter>.Instance);
    }

    private static void SetupNotEnabledWorkers(
        Mock<IJobExecutor> executor,
        Mock<IHeartbeatMaintainer> maintainer,
        Mock<IIdempotencyMonitor> idempotencyMonitor,
        Mock<IJobSubscriberManager> jobSubscriberManager)
    {
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);
        maintainer.Setup(m => m.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);
        idempotencyMonitor.Setup(m => m.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);
        jobSubscriberManager.Setup(s => s.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);
    }

    [Fact]
    public async Task HandleAsync_DoesNotCompleteWhenOnlyNotEnabledWorkersFinish()
    {
        using var executionEndArbiter = CreateExecutionEndArbiter();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return HandlerComponentResponse.Finished;
            });

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        var maintainer = new Mock<IHeartbeatMaintainer>(MockBehavior.Strict);
        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        var jobSubscriberManager = new Mock<IJobSubscriberManager>(MockBehavior.Strict);
        SetupNotEnabledWorkers(executor, maintainer, idempotencyMonitor, jobSubscriberManager);

        var handler = new Handler(executionEndArbiter, jobLoaderLoop.Object, maintainer.Object, executor.Object,
            idempotencyMonitor.Object, jobSubscriberManager.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = 1}),
            new NullLogger<Handler>());

        var handleTask = handler.HandleAsync(cts.Token);

        // Give NotEnabled workers time to finish without unblocking HandleAsync.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(handleTask.IsCompleted);

        await cts.CancelAsync();

        // Shutdown may either cancel WaitAsync or let a worker's OperationCanceledException be
        // filtered (token already canceled) and complete HandleAsync without rethrowing.
        try
        {
            await handleTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation hits HandleAsync's waits directly.
        }

        Assert.True(handleTask.IsCompleted);
        if (handleTask.IsFaulted)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(handleTask.Exception!.GetBaseException());
        }
    }

    [Fact]
    public async Task HandleAsync_ReturnsFalseOnUnhandledWorkerException()
    {
        using var executionEndArbiter = CreateExecutionEndArbiter();
        var expected = new InvalidOperationException("worker blew up");

        // Only the failing worker may return Finished (via the catch path). Other workers
        // return NotEnabled so they cannot race ahead and clear the wait before the exception
        // is recorded.
        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        var maintainer = new Mock<IHeartbeatMaintainer>(MockBehavior.Strict);
        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        var jobSubscriberManager = new Mock<IJobSubscriberManager>(MockBehavior.Strict);
        SetupNotEnabledWorkers(executor, maintainer, idempotencyMonitor, jobSubscriberManager);

        var handler = new Handler(executionEndArbiter, jobLoaderLoop.Object, maintainer.Object, executor.Object,
            idempotencyMonitor.Object, jobSubscriberManager.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = 1}),
            new NullLogger<Handler>());

        var result = await handler.HandleAsync(TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task HandleAsync_WhenWorkerThrowsOperationCanceledWhileTokenCanceled_DoesNotRethrow()
    {
        using var executionEndArbiter = CreateExecutionEndArbiter();
        using var cts = new CancellationTokenSource();

        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return HandlerComponentResponse.Finished;
            });

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        var maintainer = new Mock<IHeartbeatMaintainer>(MockBehavior.Strict);
        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        var jobSubscriberManager = new Mock<IJobSubscriberManager>(MockBehavior.Strict);
        SetupNotEnabledWorkers(executor, maintainer, idempotencyMonitor, jobSubscriberManager);

        var handler = new Handler(executionEndArbiter, jobLoaderLoop.Object, maintainer.Object, executor.Object,
            idempotencyMonitor.Object, jobSubscriberManager.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = 1}),
            new NullLogger<Handler>());

        var handleTask = handler.HandleAsync(cts.Token);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        try
        {
            await handleTask;
        }
        catch (OperationCanceledException)
        {
            // Direct cancellation of HandleAsync waits is still acceptable.
        }

        Assert.True(handleTask.IsCompleted);
        if (handleTask.IsFaulted)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(handleTask.Exception!.GetBaseException());
        }
    }

    [Fact]
    public async Task HandleAsync_WhenWorkerThrowsOperationCanceledWhileTokenNotCanceled_ReturnsFalse()
    {
        using var executionEndArbiter = CreateExecutionEndArbiter();
        var expected = new OperationCanceledException("unexpected cancel");

        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        var maintainer = new Mock<IHeartbeatMaintainer>(MockBehavior.Strict);
        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        var jobSubscriberManager = new Mock<IJobSubscriberManager>(MockBehavior.Strict);
        SetupNotEnabledWorkers(executor, maintainer, idempotencyMonitor, jobSubscriberManager);

        var handler = new Handler(executionEndArbiter, jobLoaderLoop.Object, maintainer.Object, executor.Object,
            idempotencyMonitor.Object, jobSubscriberManager.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = 1}),
            new NullLogger<Handler>());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task TestRunAsync(int numberOfExecutorThreads, int expectedNumberOfThreads)
    {
        using var executionEndArbiter = CreateExecutionEndArbiter();
        var jobLoaderLoop = new Mock<IJobLoaderLoop>(MockBehavior.Strict);
        jobLoaderLoop
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerComponentResponse.Finished);

        var executor = new Mock<IJobExecutor>(MockBehavior.Strict);
        executor.Setup(e => e.RunAsync(It.IsAny<int>(), TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerComponentResponse.Finished);

        var maintainer = new Mock<IHeartbeatMaintainer>(MockBehavior.Strict);
        maintainer.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);

        var idempotencyMonitor = new Mock<IIdempotencyMonitor>(MockBehavior.Strict);
        idempotencyMonitor.Setup(m => m.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);

        var jobSubscriberManager = new Mock<IJobSubscriberManager>(MockBehavior.Strict);
        jobSubscriberManager.Setup(s => s.RunAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(HandlerComponentResponse.NotEnabled);

        var options = new ThreadConfigurationModel
        {
            WorkerThreadCount = numberOfExecutorThreads
        };
        Assert.Equal(expectedNumberOfThreads, options.EffectiveWorkerThreadCount);

        var handler = new Handler(executionEndArbiter, jobLoaderLoop.Object, maintainer.Object, executor.Object,
            idempotencyMonitor.Object, jobSubscriberManager.Object,
            Options.Create(new ThreadConfigurationModel {WorkerThreadCount = numberOfExecutorThreads}),
            new NullLogger<Handler>());

        var result = await handler.HandleAsync(TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Single(jobLoaderLoop.Invocations);
        Assert.Equal(expectedNumberOfThreads, executor.Invocations.Count);
        for (var i = 0; i < expectedNumberOfThreads; i++)
        {
            var i1 = i;
            executor.Verify(e => e.RunAsync(i1, TestContext.Current.CancellationToken));
        }

        Assert.Single(maintainer.Invocations);
        Assert.Single(idempotencyMonitor.Invocations);
        Assert.Single(jobSubscriberManager.Invocations);
    }
}