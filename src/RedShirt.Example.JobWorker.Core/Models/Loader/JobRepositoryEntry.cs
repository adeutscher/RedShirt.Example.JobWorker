using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;

namespace RedShirt.Example.JobWorker.Core.Models.Loader;

internal interface IJobRepositoryEntry
{
    DateTime LastHeartbeatTime { get; set; }
    IJobModel JobModel { get; }
    JobState State { get; set; }
    Task<Guid> AcquireLockAsync(CancellationToken cancellationToken = default);
    Task ReleaseLockAsync(Guid lockId, CancellationToken cancellationToken = default);
}

internal class JobRepositoryEntry : IJobRepositoryEntry
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private Guid _lockId = Guid.Empty;
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