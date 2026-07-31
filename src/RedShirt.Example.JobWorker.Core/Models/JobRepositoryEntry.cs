using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;

namespace RedShirt.Example.JobWorker.Core.Models;

internal interface ISortableJobWrapper
{
    IJobModel JobModel { get; }
}

internal interface IJobRepositoryEntry : ISortableJobWrapper
{
    bool CanHeartbeat { get; }
    DateTime LastHeartbeatTime { get; set; }
    JobState State { get; }
    Task<Guid> AcquireLockAsync(CancellationToken cancellationToken = default);
    Task ReleaseLockAsync(Guid lockId, CancellationToken cancellationToken = default);
    Task SetIfFlightTimeCanBeExtendedAsync(bool flightTime, CancellationToken cancellationToken = default);
    Task SetStateAsync(JobState state, CancellationToken cancellationToken = default);
}

internal class JobRepositoryEntry : IJobRepositoryEntry
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private Guid _lockId = Guid.Empty;
    public bool CanHeartbeat { get; private set; } = true;
    public required DateTime LastHeartbeatTime { get; set; }
    public required IJobModel JobModel { get; init; }

    public async Task SetIfFlightTimeCanBeExtendedAsync(bool flightTime, CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        CanHeartbeat = flightTime;
        _semaphoreSlim.Release();
    }

    public async Task SetStateAsync(JobState state, CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        State = state;
        _semaphoreSlim.Release();
    }

    public async Task<Guid> AcquireLockAsync(CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        _lockId = Guid.NewGuid();
        return _lockId;
    }

    public Task ReleaseLockAsync(Guid lockId, CancellationToken cancellationToken = default)
    {
        if (lockId != _lockId || lockId == Guid.Empty)
        {
            throw new IllegalUnlockException();
        }

        _semaphoreSlim.Release();
        return Task.CompletedTask;
    }

    public JobState State { get; private set; } = JobState.Inactive;
}