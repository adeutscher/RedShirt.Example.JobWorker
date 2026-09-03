using RedShirt.Example.JobWorker.Connectors.Common.Http.Enums;

namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Models;

/// <summary>
///     Result of an OAuth token cache lookup, including how the token was obtained.
/// </summary>
public class OAuthTokenCacheResponse
{
    public required string AccessToken { get; init; }

    /// <summary>
    ///     UTC instant when the access token should be treated as expired.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>
    ///     Whether the token came from cache or was freshly retrieved (and whether credentials were refreshed).
    /// </summary>
    public required TokenCacheState TokenCacheState { get; init; }
}