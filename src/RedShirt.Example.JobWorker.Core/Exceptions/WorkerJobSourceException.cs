namespace RedShirt.Example.JobWorker.Core.Exceptions;

/// <summary>
///     Classified failure from a job source operation (acknowledge, heartbeat, etc.).
/// </summary>
public sealed class WorkerJobSourceException : Exception
{
    /// <summary>
    ///     When <c>true</c>, a retry wrapper inside the job source has already exhausted retries for the
    ///     underlying cause; outer Core retry layers should not retry again.
    /// </summary>
    public required bool IsHandled { get; init; }

    public required bool CouldBeTransient { get; init; }

    /// <summary>
    ///     When <c>true</c>, a possible transient or environmental cause could be resolved outside the worker
    ///     process (for example an infrastructure or IAM change) without restarting the job worker.
    /// </summary>
    public required bool CouldBeExternallySolvable { get; init; }

    public WorkerJobSourceException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public WorkerJobSourceException(string message) : base(message)
    {
    }
}