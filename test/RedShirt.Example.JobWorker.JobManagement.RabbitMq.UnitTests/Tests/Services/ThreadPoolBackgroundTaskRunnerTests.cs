using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class ThreadPoolBackgroundTaskRunnerTests
{
    [Fact]
    public async Task Run_AwaitsReturnedTaskFromWork()
    {
        var runner = new ThreadPoolBackgroundTaskRunner();
        var workEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var innerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workFullyCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = false;

        runner.Run(async () =>
        {
            workEntered.TrySetResult();
            await innerCompleted.Task;
            observed = true;
            workFullyCompleted.TrySetResult();
        }, TestContext.Current.CancellationToken);

        await workEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(observed);

        innerCompleted.TrySetResult();
        await workFullyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(observed);
    }

    [Fact]
    public async Task Run_ExecutesWorkOnThreadPoolThread()
    {
        var runner = new ThreadPoolBackgroundTaskRunner();
        var workEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedOnPoolThread = false;

        runner.Run(async () =>
        {
            workEntered.TrySetResult();
            await releaseWork.Task;
            observedOnPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            workFinished.TrySetResult();
        }, TestContext.Current.CancellationToken);

        await workEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(releaseWork.Task.IsCompleted);

        releaseWork.TrySetResult();
        await workFinished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(observedOnPoolThread);
    }

    [Fact]
    public void Run_ReturnsBeforeWorkCompletes()
    {
        var runner = new ThreadPoolBackgroundTaskRunner();
        var allowWorkToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Run(() => allowWorkToFinish.Task, CancellationToken.None);

        Assert.False(allowWorkToFinish.Task.IsCompleted);
        allowWorkToFinish.TrySetResult();
    }

    [Fact]
    public async Task Run_WhenCancellationTokenAlreadyCanceled_DoesNotInvokeWork()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var runner = new ThreadPoolBackgroundTaskRunner();
        var executed = false;

        runner.Run(() =>
        {
            executed = true;
            return Task.CompletedTask;
        }, cts.Token);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.False(executed);
    }
}