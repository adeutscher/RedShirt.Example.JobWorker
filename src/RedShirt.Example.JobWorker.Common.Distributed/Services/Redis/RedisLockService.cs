using Medallion.Threading.Redis;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Configuration;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

/// <summary>
///     Redis-based locking through the DistributedLock.Redis package.
/// </summary>
/// <param name="redisConnectionCacheService"></param>
internal class RedisLockService(
    IDistributedRetryWrapperService retryWrapper,
    IRedisConnectionCacheService redisConnectionCacheService,
    IOptions<LockConfigurationModel> options) : IAbstractedLockService
{
    public TimeSpan Timeout => options.Value.EffectiveTimeout;

    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var redis = await retryWrapper.RunAsync(redisConnectionCacheService.GetDatabaseAsync, cancellationToken);
        var redisLock = new RedisDistributedLock(lockName, redis);
        return new DistributedLock(retryWrapper, await redisLock.TryAcquireAsync(Timeout, cancellationToken));
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