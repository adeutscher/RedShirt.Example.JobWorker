namespace RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;

public sealed class SafeDistributedGetOperationResponse<T> : SafeDistributedOperationResponse
{
    public required T? Value { get; init; }
}