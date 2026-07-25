namespace RedShirt.Example.JobWorker.Core.Services.Loader;

public interface IJobLoaderStateService
{
    bool IsLoaderFinished();
    void ReportLoaderStart();
    void ReportLoaderStop();
}

internal class JobLoaderStateService : IJobLoaderStateService
{
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

    public bool IsLoaderFinished()
    {
        return _isStarted && _isFinished;
    }
}