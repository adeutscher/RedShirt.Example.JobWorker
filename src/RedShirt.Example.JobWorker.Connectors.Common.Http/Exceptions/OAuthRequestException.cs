using System.Net;

namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;

/// <summary>
///     The OAuth token endpoint returned a non-success HTTP status.
/// </summary>
public sealed class OAuthRequestException : Exception
{
    public required HttpStatusCode? StatusCode { get; init; }
    public required bool CredentialStorageProblem { get; init; }
    public required bool FreshCredentialCacheResult { get; init; }

    public OAuthRequestException(string message)
        : base(message)
    {
    }

    public OAuthRequestException(string message, Exception innerException, HttpStatusCode? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}