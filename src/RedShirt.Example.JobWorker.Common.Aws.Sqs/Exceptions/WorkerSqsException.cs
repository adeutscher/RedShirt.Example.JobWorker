namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;

/// <summary>
///     Classified failure from an SQS client operation.
/// </summary>
public sealed class WorkerSqsException : Exception
{
    /// <summary>
    ///     When <c>true</c>, a retry wrapper inside the SQS layer has already exhausted retries for the
    ///     underlying cause; outer retry layers should not retry again.
    /// </summary>
    public bool IsHandled { get; init; }

    public bool IsCritical { get; init; }
    public bool IsTransient { get; init; }

    public WorkerSqsException(
        Exception innerException,
        bool isCritical = true,
        bool isTransient = false,
        bool isHandled = false) : base(
        innerException.Message,
        innerException)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
        IsHandled = isHandled;
    }

    public WorkerSqsException(
        string message,
        bool isCritical = true,
        bool isTransient = false,
        bool isHandled = false) : base(message)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
        IsHandled = isHandled;
    }
}