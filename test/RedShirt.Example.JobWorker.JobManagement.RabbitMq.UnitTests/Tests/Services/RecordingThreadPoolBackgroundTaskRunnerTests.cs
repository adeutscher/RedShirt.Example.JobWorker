using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

[Collection("RabbitMq serial tests")]
public class RecordingThreadPoolBackgroundTaskRunnerTests
{
    [Fact]
    public async Task DrainAllAsync_RunsEnqueuedWorkInOrder()
    {
        var recording = new RecordingThreadPoolBackgroundTaskRunner();
        var order = new List<int>();

        recording.Run(() =>
        {
            order.Add(1);
            return Task.CompletedTask;
        }, CancellationToken.None);
        recording.Run(() =>
        {
            order.Add(2);
            return Task.CompletedTask;
        }, CancellationToken.None);

        await recording.DrainAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void Run_WhenCancellationTokenAlreadyCanceled_DoesNotEnqueueWork()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var recording = new RecordingThreadPoolBackgroundTaskRunner();
        var executed = false;

        recording.Run(() =>
        {
            executed = true;
            return Task.CompletedTask;
        }, cts.Token);

        Assert.False(executed);
    }

    [Fact]
    public async Task DrainAllAsync_AwaitsReturnedTaskFromWork()
    {
        var recording = new RecordingThreadPoolBackgroundTaskRunner();
        var innerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = false;

        recording.Run(async () =>
        {
            await innerCompleted.Task;
            observed = true;
        }, CancellationToken.None);

        Assert.False(observed);

        innerCompleted.TrySetResult();
        await recording.DrainAllAsync(TestContext.Current.CancellationToken);

        Assert.True(observed);
    }
}
