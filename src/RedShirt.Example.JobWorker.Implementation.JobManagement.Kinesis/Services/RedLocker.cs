using RedLockNet;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal class RedLocker(IRedisConnectionSource redisConnectionSource) : IAbstractedLocker
{
    public async Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        var distributedLockFactory = redisConnectionSource.GetLockFactory();
        return new RedLockLock(await distributedLockFactory.CreateLockAsync(KeyHelper.GetLockKey(lockName), TimeSpan.FromSeconds(30)));
    }

    internal class RedLockLock(IRedLock redLock) : IAbstractedLock
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