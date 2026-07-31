namespace RedShirt.Example.JobWorker.Common.Distributed.Models;

/// <summary>
///     Lock result from ISafeLockService
/// </summary>
public interface ISafeAbstractedLock : IAbstractedLock
{
    /// <summary>
    ///     The mainline <see cref="IAbstractedLock.IsAcquired" /> property on a permissive
    ///     implementation of the safe abstracted lock should return <c>true</c> in the event of a service failure.
    ///     However, it is possible that it may still be relevant to know whether the lock was truly acquired.
    /// </summary>
    bool IsTrulyAcquired { get; }
}