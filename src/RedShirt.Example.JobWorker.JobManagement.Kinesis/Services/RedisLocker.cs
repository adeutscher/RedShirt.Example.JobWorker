using Medallion.Threading.Redis;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal class RedisLocker(IRedisConnectionCacheService redisConnectionCacheService) : IAbstractedLocker
{
    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var redis = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);
        var redisLock = new RedisDistributedLock(KeyHelper.GetLockKey(lockName), redis);
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