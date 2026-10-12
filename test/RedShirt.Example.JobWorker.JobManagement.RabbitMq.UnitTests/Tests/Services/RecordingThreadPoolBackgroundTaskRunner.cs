using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

/// <summary>
///     Captures <see cref="IThreadPoolBackgroundTaskRunner.Run" /> work so tests can execute it deterministically.
/// </summary>
internal sealed class RecordingThreadPoolBackgroundTaskRunner : IThreadPoolBackgroundTaskRunner
{
    private readonly Queue<Func<Task>> _pending = new();
    private readonly object _lock = new();

    public void Run(Func<Task> work, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        lock (_lock)
        {
            _pending.Enqueue(work);
        }
    }

    public async Task DrainAllAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Func<Task> work;
            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                work = _pending.Dequeue();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await work().ConfigureAwait(false);
        }
    }
}
