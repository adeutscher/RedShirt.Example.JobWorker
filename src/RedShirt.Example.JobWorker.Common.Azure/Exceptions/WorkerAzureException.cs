namespace RedShirt.Example.JobWorker.Common.Azure.Exceptions;

/// <summary>
///     Classified failure from an Azure client operation.
/// </summary>
public sealed class WorkerAzureException : Exception
{
    /// <summary>
    ///     When <c>true</c>, a retry wrapper inside the Azure layer has already exhausted retries for the
    ///     underlying cause; outer retry layers should not retry again.
    /// </summary>
    public required bool IsHandled { get; init; }

    public required bool CouldBeTransient { get; init; }

    /// <summary>
    ///     When <c>true</c>, a possible transient or environmental cause could be resolved outside the worker
    ///     process (for example an infrastructure or IAM change) without restarting the job worker.
    /// </summary>
    public required bool CouldBeExternallySolvable { get; init; }

    public WorkerAzureException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public WorkerAzureException(string message) : base(message)
    {
    }
}