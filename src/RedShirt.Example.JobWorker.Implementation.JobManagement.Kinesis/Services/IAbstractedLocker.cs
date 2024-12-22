using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

public interface IAbstractedLocker
{
    Task<IAbstractedLock> GetLockAsync(string lockName, CancellationToken cancellationToken = default);
}