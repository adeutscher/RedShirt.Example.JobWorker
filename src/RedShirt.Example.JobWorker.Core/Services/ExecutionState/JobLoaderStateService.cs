namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Describes to downstream classes the state of the job loading loop without creating a circular dependency.
/// </summary>
internal interface IJobLoaderStateReaderService
{
    bool HasLoaderStarted();

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
    ///     Multithreading protection.
    ///     Feels a little silly for a service that only sets booleans to true, but it makes automated audits happy.
    /// </summary>
    private readonly Lock _lock = new();

    private bool _isFinished;

    private bool _isStarted;

    public void ReportLoaderStart()
    {
        lock (_lock)
        {
            _isStarted = true;
        }
    }

    public void ReportLoaderStop()
    {
        lock (_lock)
        {
            _isFinished = true;
        }
    }

    public bool HasLoaderStarted()
    {
        lock (_lock)
        {
            return _isStarted;
        }
    }

    public bool IsLoaderFinished()
    {
        lock (_lock)
        {
            return _isStarted && _isFinished;
        }
    }
}