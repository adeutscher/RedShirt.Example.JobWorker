using Medallion.Threading.Redis;
using RedShirt.Example.JobWorker.Common.Distributed.Models;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

public interface IAbstractedLocker
{
    Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default);
}

internal class RedisLocker(IRedisConnectionCacheService redisConnectionCacheService) : IAbstractedLocker
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