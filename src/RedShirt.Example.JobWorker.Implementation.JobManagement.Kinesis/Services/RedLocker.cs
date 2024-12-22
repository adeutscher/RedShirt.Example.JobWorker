using RedLockNet;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal class RedLocker(IRedisConnectionSource redisConnectionSource) : IAbstractedLocker
{
    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var redis = redisConnectionSource.GetLockFactory();
        return new RedLockLock(await redis.CreateLockAsync(lockName, TimeSpan.FromSeconds(30)));
    }

    public class RedLockLock(IRedLock redLock) : IAbstractedLock
    {
        public bool IsAcquired => redLock.IsAcquired;

        public void Unlock()
        {
            if (IsAcquired)
            {
                redLock.Dispose();
            }
        }
    }
}