namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal interface IAbstractedLock
{
    bool IsAcquired { get; }
    void Unlock();
}