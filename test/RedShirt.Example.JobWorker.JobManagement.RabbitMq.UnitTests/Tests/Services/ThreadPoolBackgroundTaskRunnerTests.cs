using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class ThreadPoolBackgroundTaskRunnerTests
{
    [Fact]
    public async Task Run_ExecutesWorkOnAnotherThread()
    {
        var runner = new ThreadPoolBackgroundTaskRunner();
        var callerThreadId = Environment.CurrentManagedThreadId;
        var workerThreadId = default(int?);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Run(async () =>
        {
            workerThreadId = Environment.CurrentManagedThreadId;
            await Task.Yield();
            completed.TrySetResult();
        }, TestContext.Current.CancellationToken);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(workerThreadId);
        Assert.NotEqual(callerThreadId, workerThreadId.Value);
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

    [Fact]
    public async Task Run_AwaitsReturnedTaskFromWork()
    {
        var runner = new ThreadPoolBackgroundTaskRunner();
        var innerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workFullyCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = false;

        runner.Run(async () =>
        {
            await innerCompleted.Task;
            observed = true;
            workFullyCompleted.TrySetResult();
        }, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(observed);

        innerCompleted.TrySetResult();
        await workFullyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(observed);
    }
}
