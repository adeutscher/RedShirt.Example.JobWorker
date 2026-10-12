namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Runs fire-and-forget work off the caller thread (for example, to reconnect after a connection shutdown).
///     
/// </summary>
internal interface IThreadPoolBackgroundTaskRunner
{
    void Run(Func<Task> work, CancellationToken cancellationToken);
}

internal sealed class ThreadPoolBackgroundTaskRunner : IThreadPoolBackgroundTaskRunner
{
    public void Run(Func<Task> work, CancellationToken cancellationToken)
    {
        _ = Task.Run(work, cancellationToken);
    }
}
