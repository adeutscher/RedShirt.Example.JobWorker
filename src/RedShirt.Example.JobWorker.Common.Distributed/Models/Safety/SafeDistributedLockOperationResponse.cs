namespace RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;

public sealed class SafeDistributedLockOperationResponse : SafeDistributedOperationResponse
{
    public required IAbstractedLock Lock { get; init; }
}