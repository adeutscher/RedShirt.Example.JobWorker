namespace RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;

/// <summary>
///     Classified failure from a Bar connector operation. Thrown by the connector implementation after
///     retry/arbitration so callers can react to a stable, already-handled outcome.
/// </summary>
public class BarException : Exception
{
    /// <summary>
    ///     When <c>true</c>, a retry wrapper inside the connector layer has already exhausted retries for the
    ///     underlying cause; outer retry layers should not retry again.
    /// </summary>
    public bool IsHandled { get; init; }

    /// <summary>
    ///     When <c>true</c>, suggests a possible transient or environmental cause could be resolved outside the application
    ///     process (with an infrastructure change, for example) without restarting the application.
    /// </summary>
    public bool CouldBeTransient { get; init; }

    /// <summary>
    ///     When <c>true</c>, suggests a possible environmental cause that could be resolved outside the application
    ///     process (for example an infrastructure change) without restarting the application.
    /// </summary>
    public bool CouldBeExternallySolvable { get; init; }

    public BarException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public BarException(string message) : base(message)
    {
    }
}