using RedShirt.Example.JobWorker.Common.Distributed.Models;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

/// <summary>
///     Wrapper around lock abstraction to be used in non-essential where a 'best effort' is good enough.
///     If the underlying cache service is not available, do not interrupt application operation.
///     Critical connection exceptions shall still be thrown by this safety layer.
/// </summary>
public interface ISafeAbstractedLockService
{
    Task<ISafeAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default);
}