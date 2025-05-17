namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

public interface IAbstractedLock
{
    bool IsAcquired { get; }
    void Unlock();
}