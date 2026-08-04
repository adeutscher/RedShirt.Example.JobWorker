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

    /// <summary>
    ///     When <c>true</c>, suggests a possible transient or environmental cause could be resolved outside the application
    ///     process (with an infrastructure change, for example) without restarting the application.
    /// </summary>
    public required bool CouldBeTransient { get; init; }

    /// <summary>
    ///     When <c>true</c>, suggests a possible environmental cause that could be resolved outside the application
    ///     process (for example an infrastructure change, for example) without restarting the application.
    /// </summary>
    public required bool CouldBeExternallySolvable { get; init; }

    public WorkerAzureException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public WorkerAzureException(string message) : base(message)
    {
    }
}