namespace RedShirt.Example.JobWorker.Common.Distributed.Models;

public interface IAbstractedLock
{
    bool IsAcquired { get; }
    Task UnlockAsync(CancellationToken cancellationToken = default);
}