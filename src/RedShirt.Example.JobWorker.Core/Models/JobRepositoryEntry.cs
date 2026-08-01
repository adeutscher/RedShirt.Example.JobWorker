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
    DateTime LastHeartbeatTime { get; set; }
    JobState State { get; }
    Task SetAsCannotHeartbeatAsync(CancellationToken cancellationToken = default);
    Task SetStateAsync(JobState state, CancellationToken cancellationToken = default);
}

internal class JobRepositoryEntry : IJobRepositoryEntry
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    public required IRawJobModel RawJobModel { get; init; }
    public bool CanHeartbeat { get; private set; } = true;
    public required DateTime LastHeartbeatTime { get; set; }
    public required IJobModel JobModel { get; init; }

    public async Task SetAsCannotHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        CanHeartbeat = false;
        _semaphoreSlim.Release();
    }

    public async Task SetStateAsync(JobState state, CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        State = state;
        _semaphoreSlim.Release();
    }

    public JobState State { get; private set; } = JobState.Inactive;
}
