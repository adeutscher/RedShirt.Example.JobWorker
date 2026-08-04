namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

/// <summary>
///     Classified failure from a distributed (cache / lock) operation.
/// </summary>
public sealed class WorkerDistributedException : Exception
{
    /// <summary>
    ///     When <c>true</c>, a retry wrapper inside the distributed layer has already exhausted retries for
    ///     the underlying cause; outer retry layers should not retry again.
    /// </summary>
    public required bool IsHandled { get; init; }

    public required bool CouldBeTransient { get; init; }

    /// <summary>
    ///     When <c>true</c>, a possible transient or environmental cause could be resolved outside the worker
    ///     process (for example an infrastructure or IAM change) without restarting the job worker.
    /// </summary>
    public required bool CouldBeExternallySolvable { get; init; }

    public WorkerDistributedException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public WorkerDistributedException(string message) : base(message)
    {
    }
}