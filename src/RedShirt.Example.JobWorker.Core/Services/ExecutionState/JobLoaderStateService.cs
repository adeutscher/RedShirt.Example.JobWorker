namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Describes to downstream classes the state of the job loading loop without creating a circular dependency.
/// </summary>
internal interface IJobLoaderStateReaderService
{
    /// <summary>
    ///     Thread-safe addition of callback actions invoked once the loader has both started and stopped.
    ///     If the loader is already finished, the callback is invoked immediately.
    /// </summary>
    /// <param name="callback"></param>
    void AddOnFinishCallback(Action callback);

    bool IsLoaderFinished();
}

/// <summary>
///     Describes to downstream workers the state of the job loading loop without creating a circular dependency.
///     This interface should only be used by the worker loop. If you need to read from state, use
///     <see cref="IJobLoaderStateReaderService" />
/// </summary>
internal interface IJobLoaderStateService : IJobLoaderStateReaderService
{
    void ReportLoaderStart();
    void ReportLoaderStop();
}

internal sealed class JobLoaderStateService : IJobLoaderStateService
{
    /// <summary>
    ///     Multithreading protection for start/stop flags and finish callbacks.
    /// </summary>
    private readonly Lock _lock = new();

    private bool _isFinished;

    private bool _isStarted;

    private Action? _onFinishCallbacks;

    private bool IsFinishedUnsafe()
    {
        return _isStarted && _isFinished;
    }

    /// <summary>
    ///     Detaches finish callbacks if the loader is finished.
    ///     Must be safe to call with or without the caller already holding <see cref="_lock" />
    ///     (<see cref="Lock" /> is reentrant).
    /// </summary>
    private Action? TakeCallbacksIfFinished()
    {
        lock (_lock)
        {
            if (!IsFinishedUnsafe())
            {
                return null;
            }

            return Interlocked.Exchange(ref _onFinishCallbacks, null);
        }
    }

    private static void InvokeCallbacks(Action? callbacks)
    {
        if (callbacks is null)
        {
            return;
        }

        foreach (var invocation in callbacks.GetInvocationList())
        {
            ((Action) invocation)();
        }
    }

    public void ReportLoaderStart()
    {
        Action? callbacks;
        lock (_lock)
        {
            _isStarted = true;
            callbacks = TakeCallbacksIfFinished();
        }

        InvokeCallbacks(callbacks);
    }

    public void ReportLoaderStop()
    {
        Action? callbacks;
        lock (_lock)
        {
            _isFinished = true;
            callbacks = TakeCallbacksIfFinished();
        }

        InvokeCallbacks(callbacks);
    }

    public bool IsLoaderFinished()
    {
        lock (_lock)
        {
            return IsFinishedUnsafe();
        }
    }

    public void AddOnFinishCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_lock)
        {
            if (!IsFinishedUnsafe())
            {
                _onFinishCallbacks += callback;
                return;
            }
        }

        callback();
    }
}