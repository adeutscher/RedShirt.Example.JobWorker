namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;

/// <summary>
///     The OAuth token endpoint response could not be parsed or lacked a usable access token.
/// </summary>
public sealed class OAuthRequestJsonException : Exception
{
    public OAuthRequestJsonException(string message) : base(message)
    {
    }

    public OAuthRequestJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}