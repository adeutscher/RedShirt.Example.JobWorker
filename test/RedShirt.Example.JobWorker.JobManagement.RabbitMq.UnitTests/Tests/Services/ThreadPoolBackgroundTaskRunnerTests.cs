using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

/// <summary>
///     Covers <see cref="ThreadPoolBackgroundTaskRunner" /> without waiting on thread-pool scheduling.
/// </summary>
[Collection("RabbitMq serial tests")]
public class ThreadPoolBackgroundTaskRunnerTests
{
    [Fact]
    public void Run_ReturnsBeforeWorkCompletes()
    {
        var runner = new ThreadPoolBackgroundTaskRunner();
        var allowWorkToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Run(() => allowWorkToFinish.Task, CancellationToken.None);

        Assert.False(allowWorkToFinish.Task.IsCompleted);
        allowWorkToFinish.TrySetResult();
    }
}