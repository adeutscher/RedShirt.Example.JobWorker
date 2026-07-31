using RedShirt.Example.JobWorker.Common.Distributed.Models;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

public interface IAbstractedLockService
{
    TimeSpan Timeout { get; }
    Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default);
}