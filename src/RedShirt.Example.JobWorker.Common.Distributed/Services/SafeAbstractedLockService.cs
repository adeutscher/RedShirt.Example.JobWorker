using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class SafeAbstractedLockService(
    ISafetyDisgraceStateService safetyDisgraceStateService,
    IAbstractedLockService lockService,
    ILogger<SafeAbstractedLockService> logger)
    : ISafeAbstractedLockService
{
    private static readonly TimeSpan LockAttemptThreshold = TimeSpan.FromSeconds(5);

    public async Task<ISafeAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        if (safetyDisgraceStateService.IsInDisgracePeriod())
        {
            return new PermissiveLock();
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var innerLock = await lockService.GetLockAsync(lockName, cancellationToken);
            stopwatch.Stop();

            /*
             * The underlying DistributedLock.Redis-based TryAcquireAsync method consumes all exceptions internally, so we are forced
             * to make a judgement call on the disgrace period based on the time that the attempt took.
             */
            var timeExceeded = stopwatch.Elapsed > LockAttemptThreshold;
            if (timeExceeded)
            {
                logger.LogWarning("Failure to communicate with lock service: Timeout");
                safetyDisgraceStateService.EnterDisgracePeriod();
            }

            return new SafeLockWrapper(innerLock, timeExceeded);
        }
        catch (WorkerDistributedException e)
        {
            logger.LogWarning(e, "Failure to communicate with lock service: {EMessage}", e.Message);
            safetyDisgraceStateService.EnterDisgracePeriod();
            return new PermissiveLock();
        }
    }

    private sealed class PermissiveLock : ISafeAbstractedLock
    {
        public bool IsAcquired => true;
        public bool IsTrulyAcquired => false;

        public void Unlock()
        {
        }
    }

    private class SafeLockWrapper(IAbstractedLock abstractedLock, bool isAcquiredOverride) : ISafeAbstractedLock
    {
        public bool IsAcquired => isAcquiredOverride || abstractedLock.IsAcquired;

        public void Unlock()
        {
            abstractedLock.Unlock();
        }

        public bool IsTrulyAcquired => abstractedLock.IsAcquired;
    }
}