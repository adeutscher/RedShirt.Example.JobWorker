using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs.Subscriptions;

public class JobSubscriberIntakeQueueTests
{
    private static (JobSubscriberIntakeQueue Queue, Action<Exception?> RaiseStop) CreateQueue()
    {
        Action<Exception?>? onStop = null;

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()))
            .Callback<Action<Exception?>>(callback => onStop = callback);

        var queue = new JobSubscriberIntakeQueue(executionEndArbiter.Object);

        Assert.NotNull(onStop);
        return (queue, onStop!);
    }

    private static IJobSourceResponse CreateResponse()
    {
        return new Mock<IJobSourceResponse>(MockBehavior.Strict).Object;
    }

    [Fact]
    public void Constructor_RegistersOnStopCallback()
    {
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()));

        _ = new JobSubscriberIntakeQueue(executionEndArbiter.Object);

        executionEndArbiter.Verify(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()), Times.Once);
    }

    [Fact(Timeout = 2000)]
    public async Task Load_ThenGetNextAsync_ReturnsLoadedResponse()
    {
        var (queue, _) = CreateQueue();
        var response = CreateResponse();

        queue.Load(response);

        var result = await queue.GetNextAsync(TestContext.Current.CancellationToken);

        Assert.Same(response, result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetNextAsync_ReturnsLoadedResponsesInFifoOrder()
    {
        var (queue, _) = CreateQueue();
        var first = CreateResponse();
        var second = CreateResponse();
        var third = CreateResponse();

        queue.Load(first);
        queue.Load(second);
        queue.Load(third);

        Assert.Same(first, await queue.GetNextAsync(TestContext.Current.CancellationToken));
        Assert.Same(second, await queue.GetNextAsync(TestContext.Current.CancellationToken));
        Assert.Same(third, await queue.GetNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 2000)]
    public async Task GetNextAsync_WaitsUntilLoadProvidesAResponse()
    {
        var (queue, _) = CreateQueue();
        var response = CreateResponse();

        var getTask = queue.GetNextAsync(TestContext.Current.CancellationToken);

        Assert.False(getTask.IsCompleted);

        queue.Load(response);

        Assert.Same(response, await getTask);
    }

    [Fact(Timeout = 2000)]
    public async Task GetNextAsync_WhenStoppedAndEmpty_ReturnsNull()
    {
        var (queue, raiseStop) = CreateQueue();

        var getTask = queue.GetNextAsync(TestContext.Current.CancellationToken);
        Assert.False(getTask.IsCompleted);

        raiseStop(null);

        Assert.Null(await getTask);
    }

    [Fact(Timeout = 2000)]
    public async Task GetNextAsync_WhenStoppedWithQueuedItems_DrainsThenReturnsNull()
    {
        var (queue, raiseStop) = CreateQueue();
        var first = CreateResponse();
        var second = CreateResponse();

        queue.Load(first);
        queue.Load(second);
        raiseStop(new InvalidOperationException("shutting down"));

        Assert.Same(first, await queue.GetNextAsync(TestContext.Current.CancellationToken));
        Assert.Same(second, await queue.GetNextAsync(TestContext.Current.CancellationToken));
        Assert.Null(await queue.GetNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 2000)]
    public async Task GetNextAsync_AfterDrainAndStop_ReturnsNull()
    {
        var (queue, raiseStop) = CreateQueue();
        var response = CreateResponse();

        queue.Load(response);
        Assert.Same(response, await queue.GetNextAsync(TestContext.Current.CancellationToken));

        raiseStop(null);

        Assert.Null(await queue.GetNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 2000)]
    public async Task GetNextAsync_WhenCancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        var (queue, _) = CreateQueue();
        using var cts = new CancellationTokenSource();

        var getTask = queue.GetNextAsync(cts.Token);
        Assert.False(getTask.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => getTask);
    }

    [Fact(Timeout = 2000)]
    public async Task Load_AfterStop_StillAllowsQueuedItemToBeRetrievedOnce()
    {
        var (queue, raiseStop) = CreateQueue();
        var response = CreateResponse();

        raiseStop(null);
        queue.Load(response);

        Assert.Same(response, await queue.GetNextAsync(TestContext.Current.CancellationToken));
        Assert.Null(await queue.GetNextAsync(TestContext.Current.CancellationToken));
    }
}
