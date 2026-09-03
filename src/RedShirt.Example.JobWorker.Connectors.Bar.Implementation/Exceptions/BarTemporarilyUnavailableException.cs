namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;

/// <summary>
///     Bar is assumed to be unavailable for the time being (for example after auth or token
///     recovery failed within the refresh cooldown window).
/// </summary>
internal sealed class BarTemporarilyUnavailableException : BarReasonToWaitException
{
    public BarTemporarilyUnavailableException() : base("Bar is assumed to be unavailable for the time being.")
    {
        IsHandled = false;
        CouldBeTransient = false;
        CouldBeExternallySolvable = true;
    }
}