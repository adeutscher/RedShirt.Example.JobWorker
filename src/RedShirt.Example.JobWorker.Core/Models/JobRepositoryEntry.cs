using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Models;

internal interface ISortableJobWrapper
{
    IJobModel JobModel { get; }
}

internal interface IJobRepositoryEntry : ISortableJobWrapper
{
    IRawJobModel RawJobModel { get; }
    bool CanHeartbeat { get; }
    DateTime LastHeartbeatTime { get; }
    JobState State { get; }
    Task SetAsCannotHeartbeatAsync(CancellationToken cancellationToken = default);
    Task SetLastHeartbeatTimeAsync(DateTime lastHeartbeatTime, CancellationToken cancellationToken = default);
    Task SetStateAsync(JobState state, CancellationToken cancellationToken = default);
}

internal class JobRepositoryEntry : IJobRepositoryEntry
{
    /// <summary>
    /// Thread-safety measure for field updates.
    /// </summary>
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    public required IRawJobModel RawJobModel { get; init; }
    public bool CanHeartbeat { get; private set; } = true;
    public DateTime LastHeartbeatTime { get; private set; }
    public required IJobModel JobModel { get; init; }

    public async Task SetAsCannotHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            CanHeartbeat = false;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public async Task SetLastHeartbeatTimeAsync(DateTime lastHeartbeatTime,
        CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            LastHeartbeatTime = lastHeartbeatTime;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public async Task SetStateAsync(JobState state, CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            State = state;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public JobState State { get; private set; } = JobState.Inactive;
}
