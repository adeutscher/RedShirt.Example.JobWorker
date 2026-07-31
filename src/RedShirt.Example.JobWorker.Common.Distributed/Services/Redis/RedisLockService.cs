using Medallion.Threading.Redis;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Configuration;
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
        return new DistributedLock(await redisLock.TryAcquireAsync(Timeout, cancellationToken));
    }

    private sealed class DistributedLock(RedisDistributedLockHandle? lockHandle) : IAbstractedLock
    {
        public bool IsAcquired => lockHandle is not null;

        public Task UnlockAsync()
        {
            if (lockHandle is null)
            {
                return Task.CompletedTask;
            }

            return lockHandle.DisposeAsync().AsTask();
        }
    }
}