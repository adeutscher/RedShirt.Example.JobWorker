namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

public interface IAbstractedLock
{
    bool IsAcquired { get; }
    void Unlock();
}