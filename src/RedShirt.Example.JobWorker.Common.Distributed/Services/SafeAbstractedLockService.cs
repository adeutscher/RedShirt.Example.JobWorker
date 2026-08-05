using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Distributed.Enums;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class SafeAbstractedLockService(
    ISafetyDisgraceStateService safetyDisgraceStateService,
    IAbstractedLockService lockService,
    ILogger<SafeAbstractedLockService> logger)
    : ISafeAbstractedLockService
{
    private TimeSpan LockAttemptThreshold => lockService.Timeout;

    public async Task<SafeDistributedLockOperationResponse> GetLockAsync(string lockName,
        CancellationToken cancellationToken = default)
    {
        if (safetyDisgraceStateService.IsInDisgracePeriod(out var nextAttemptTime))
        {
            return new SafeDistributedLockOperationResponse
            {
                Result = SafeDistributedOperationResult.DisgracePeriod,
                NextAttemptTime = nextAttemptTime,
                Lock = new PermissiveLock()
            };
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
            // ReSharper disable once InvertIf
            if (!innerLock.IsAcquired && stopwatch.Elapsed > LockAttemptThreshold)
            {
                // Not inverting the if statement because I think it reads more nicely this way.

                logger.LogWarning("Failure to communicate with lock service: Timeout");
                safetyDisgraceStateService.EnterDisgracePeriod();

                return new SafeDistributedLockOperationResponse
                {
                    Result = SafeDistributedOperationResult.Failure,
                    NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime(),
                    Lock = new SafeLockWrapper(innerLock, true)
                };
            }

            // If we have gotten to this point, then we either got the lock or had a timely failed attempt.

            // Refresh nextAttemptTime; it may have drifted if the lock took a moment to acquire.
            return new SafeDistributedLockOperationResponse
            {
                Result = SafeDistributedOperationResult.Success,
                NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime(),
                Lock = new SafeLockWrapper(innerLock, false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkerDistributedException e)
        {
            logger.LogWarning(e, "Failure to communicate with lock service: {EMessage}", e.Message);
            safetyDisgraceStateService.EnterDisgracePeriod();
            return new SafeDistributedLockOperationResponse
            {
                Result = SafeDistributedOperationResult.Failure,
                NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime(),
                Lock = new PermissiveLock()
            };
        }
    }

    private sealed class PermissiveLock : IAbstractedLock
    {
        public bool IsAcquired => true;

        public Task UnlockAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SafeLockWrapper(IAbstractedLock abstractedLock, bool forceAcquired) : IAbstractedLock
    {
        public bool IsAcquired => forceAcquired || abstractedLock.IsAcquired;

        public Task UnlockAsync(CancellationToken cancellationToken = default)
        {
            return abstractedLock.UnlockAsync(cancellationToken);
        }
    }
}