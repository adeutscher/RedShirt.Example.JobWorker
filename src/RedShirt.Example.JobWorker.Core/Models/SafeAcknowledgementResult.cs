namespace RedShirt.Example.JobWorker.Core.Models;

internal interface ISafeAcknowledgementResult
{
    bool? LoggedFailureSuccessfully { get; init; }
    bool AcknowledgedSuccessfully { get; init; }

    /// <summary>
    ///     Summary.
    /// </summary>
    /// <returns></returns>
    bool Success { get; }
}

internal class SafeAcknowledgementResult : ISafeAcknowledgementResult
{
    public required bool? LoggedFailureSuccessfully { get; init; }
    public required bool AcknowledgedSuccessfully { get; init; }

    /// <summary>
    ///     Summary.
    /// </summary>
    /// <returns></returns>
    public bool Success => (LoggedFailureSuccessfully ?? true) && AcknowledgedSuccessfully;
}