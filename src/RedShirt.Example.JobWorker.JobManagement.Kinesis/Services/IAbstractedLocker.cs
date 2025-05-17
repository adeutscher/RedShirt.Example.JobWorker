using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

public interface IAbstractedLocker
{
    Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default);
}