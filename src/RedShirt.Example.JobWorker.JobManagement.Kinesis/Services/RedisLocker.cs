using Medallion.Threading.Redis;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal class RedisLocker(IRedisConnectionSource redisConnectionSource) : IAbstractedLocker
{
    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var redis = redisConnectionSource.GetDatabase();
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