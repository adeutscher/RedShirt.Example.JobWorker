using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;

/// <summary>
///     The Bar dependency rejected the bearer token (HTTP 401), including after a force-refresh attempt.
///     Surfaced to callers as <see cref="BarException" /> by the connector retry wrapper.
/// </summary>
internal sealed class BarUnauthorizedException : BarException
{
    public BarUnauthorizedException() : base("Bar API rejected the bearer token.")
    {
        IsHandled = false;
        CouldBeTransient = false;
        CouldBeExternallySolvable = true;
    }
}