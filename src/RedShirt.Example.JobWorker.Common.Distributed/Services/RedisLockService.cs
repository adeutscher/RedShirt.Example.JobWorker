using Medallion.Threading.Redis;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class RedisLockService(IRedisConnectionCacheService redisConnectionCacheService) : IAbstractedLockService
{
    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var redis = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);
        var redisLock = new RedisDistributedLock(lockName, redis);
        return new DistributedLock(await redisLock.TryAcquireAsync(cancellationToken: cancellationToken));
    }

    private sealed class DistributedLock(RedisDistributedLockHandle? lockHandle) : IAbstractedLock
    {
        public bool IsAcquired => lockHandle is not null;

        public void Unlock()
        {
            lockHandle?.Dispose();
        }
    }
}