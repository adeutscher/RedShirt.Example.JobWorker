namespace RedShirt.Example.JobWorker.Core.Utility;

public class AsyncAutoResetEvent(bool setInitially = false)
{
    private readonly Queue<TaskCompletionSource<bool>> _waits = new();
    private bool _signaled = setInitially;

    private void RemoveWaiter(TaskCompletionSource<bool> tcs)
    {
        lock (_waits)
        {
            // If the task was already completed by Set(), we don't need to do anything
            if (tcs.Task.IsCompleted)
            {
                return;
            }

            // Remove the specific cancelled waiter from the queue
            // A custom queue or LinkedList would optimize this, but List/Queue manipulation under a lock is robust for typical use
            var remaining = new List<TaskCompletionSource<bool>>(_waits.Count);
            while (_waits.Count > 0)
            {
                var current = _waits.Dequeue();
                if (current != tcs)
                {
                    remaining.Add(current);
                }
            }

            foreach (var waiter in remaining)
            {
                _waits.Enqueue(waiter);
            }

            // Cancel the task so the WaitAsync awaiter wakes up and returns or throws
            tcs.TrySetCanceled();
        }
    }

    public void Set()
    {
        TaskCompletionSource<bool>? toRelease = null;

        lock (_waits)
        {
            if (_waits.Count > 0)
            {
                // Release the first waiter in line
                toRelease = _waits.Dequeue();
            }
            else if (!_signaled)
            {
                // No waiters, store the signal for the next caller
                _signaled = true;
            }
        }

        // Complete the task outside the lock to avoid deadlocks
        toRelease?.TrySetResult(true);
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> tcs;

        lock (_waits)
        {
            // 1. Fast path: Event is already signalled
            if (_signaled)
            {
                _signaled = false;
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }

            // 2. Fast path: Immediate timeout
            if (timeout == TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            // 3. Slow path: Enqueue a new waiter
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waits.Enqueue(tcs);
        }

        // 4. Handle infinite waits with no cancellation token
        if (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
        {
            return await tcs.Task.ConfigureAwait(false);
        }

        // 5. Manage timeout and token monitoring
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            cts.CancelAfter(timeout);
        }

        // Register a cancellation callback to pull this waiter out of the queue if it times out or gets cancelled
        await using var registration = cts.Token.Register(state =>
        {
            var tuple = ((AsyncAutoResetEvent Event, TaskCompletionSource<bool> Tcs)) state!;
            tuple.Event.RemoveWaiter(tuple.Tcs);
        }, (this, tcs));

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Explicit user cancellation
            throw;
        }
        catch (OperationCanceledException)
        {
            // Internal cancellation caused by timeout expiring
            return false;
        }
    }
}