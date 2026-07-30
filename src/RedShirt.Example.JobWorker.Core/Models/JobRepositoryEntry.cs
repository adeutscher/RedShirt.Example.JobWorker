using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;

namespace RedShirt.Example.JobWorker.Core.Models;

internal interface ISortableJobWrapper
{
    IJobModel JobModel { get; }
}

internal interface IJobRepositoryEntry : ISortableJobWrapper
{
    bool FlightTimeCanBeExtended { get; set; }
    DateTime LastHeartbeatTime { get; set; }
    JobState State { get; set; }
    Task<Guid> AcquireLockAsync(CancellationToken cancellationToken = default);
    Task ReleaseLockAsync(Guid lockId, CancellationToken cancellationToken = default);
}

internal class JobRepositoryEntry : IJobRepositoryEntry
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private Guid _lockId = Guid.Empty;
    public required bool FlightTimeCanBeExtended { get; set; }
    public required DateTime LastHeartbeatTime { get; set; }
    public required IJobModel JobModel { get; init; }

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

    public required JobState State { get; set; }
}