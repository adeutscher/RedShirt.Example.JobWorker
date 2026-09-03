namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Models;

/// <summary>
///     Result of a successful OAuth client-credentials token request.
/// </summary>
public class OAuthTokenResponse
{
    public required string AccessToken { get; init; }

    /// <summary>
    ///     UTC instant when the access token should be treated as expired.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}