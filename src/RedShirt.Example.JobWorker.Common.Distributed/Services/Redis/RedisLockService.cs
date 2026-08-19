using Medallion.Threading.Redis;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Configuration;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis.Resilience;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

/// <summary>
///     Redis-based locking through the DistributedLock.Redis package.
/// </summary>
/// <param name="redisConnectionCacheService"></param>
internal sealed class RedisLockService(
    IDistributedRetryWrapperService retryWrapper,
    IRedisConnectionCacheService redisConnectionCacheService,
    IOptions<LockConfigurationModel> options) : IAbstractedLockService
{
    /// <summary>
    ///     Maximum timeout after which a lock will be considered failed.
    ///     Reaching a timeout implies an unstable connection to the lock service,
    ///     a stable connection that failed to acquire a lock is expected to immediately exit.
    /// </summary>
    public TimeSpan Timeout => options.Value.EffectiveTimeout;

    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var redis = await retryWrapper.RunAsync(redisConnectionCacheService.GetDatabaseAsync, cancellationToken);
        var redisLock = new RedisDistributedLock(lockName, redis);

        // The timeout in this context is to give leeway for possible network errors.
        // A stable connection is expected to immediately fail to acquire a lock.
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var constrainedCompositeCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            return new DistributedLock(retryWrapper,
                await redisLock.TryAcquireAsync(cancellationToken: constrainedCompositeCts.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Account for max timeout
            return new DistributedLock(retryWrapper, null);
        }
    }

    internal sealed class DistributedLock(
        IDistributedRetryWrapperService retryWrapper,
        RedisDistributedLockHandle? lockHandle) : IAbstractedLock
    {
        public bool IsAcquired => lockHandle is not null;

        public async Task UnlockAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (lockHandle is null)
            {
                return;
            }

            try
            {
                await retryWrapper.RunAsync(_ => lockHandle.DisposeAsync().AsTask(), cancellationToken);
            }
            catch (WorkerDistributedException)
            {
                /*
                 * Pass if the retry fails
                 * Nothing really to do. We're throwing away the lock anyway.
                 *
                 * I haven't been able to reproduce an exception here in local testing
                 * since I've switched Unlock to be UnlockAsync,
                 * but I have no reason to believe that it couldn't do it again.
                 */
            }
        }
    }
}