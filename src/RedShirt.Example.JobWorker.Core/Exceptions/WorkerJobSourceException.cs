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
    public bool IsHandled { get; init; }

    public bool IsCritical { get; init; }
    public bool CouldBeTransient { get; init; }

    public WorkerJobSourceException(
        Exception innerException,
        bool isCritical = true,
        bool couldBeTransient = false,
        bool isHandled = false) : base(innerException.Message, innerException)
    {
        IsCritical = isCritical;
        CouldBeTransient = couldBeTransient;
        IsHandled = isHandled;
    }

    public WorkerJobSourceException(
        string message,
        bool isCritical = true,
        bool couldBeTransient = false,
        bool isHandled = false) : base(message)
    {
        IsCritical = isCritical;
        CouldBeTransient = couldBeTransient;
        IsHandled = isHandled;
    }
}