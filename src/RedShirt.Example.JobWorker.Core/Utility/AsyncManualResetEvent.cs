namespace RedShirt.Example.JobWorker.Core.Utility;

public class AsyncManualResetEvent
{
    private volatile TaskCompletionSource<object?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AsyncManualResetEvent(bool setInitially = false)
    {
        if (setInitially)
        {
            _tcs.TrySetResult(null);
        }
    }

    public void Reset()
    {
        while (true)
        {
            var tcs = _tcs;
            if (!tcs.Task.IsCompleted ||
                Interlocked.CompareExchange(ref _tcs,
                    new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously), tcs) == tcs)
            {
                return;
            }
        }
    }

    public void Set()
    {
        _tcs.TrySetResult(null);
    }

    public Task<bool> WaitAsync(CancellationToken cancellationToken = default)
    {
        return WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // 1. Fast path: Event is already signalled
        var eventTask = _tcs.Task;
        if (eventTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        // 2. Fast path: Timeout is zero
        if (timeout == TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        // 3. Fast path: Infinite timeout with no token
        if (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
        {
            await eventTask.ConfigureAwait(false);
            return true;
        }

        // 4. Slow path: Link event task with timeout and cancellation
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            cts.CancelAfter(timeout);
        }

        // Create a task that completes when cancellation/timeout is triggered
        var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        var completedTask = await Task.WhenAny(eventTask, delayTask).ConfigureAwait(false);

        if (completedTask == eventTask)
        {
            return true;
        }

        // Delay task won: either user cancellation or timeout.
        // WhenAny does not throw when the delay task is cancelled, so check explicitly.
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}