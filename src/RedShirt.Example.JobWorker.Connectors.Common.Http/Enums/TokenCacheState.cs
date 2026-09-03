namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Enums;

/// <summary>
///     Describes how an OAuth access token was obtained for a cache lookup.
/// </summary>
public enum TokenCacheState
{
    /// <summary>
    ///     Client credentials were force-refreshed from the secret manager and a new token was requested.
    /// </summary>
    ForcedCredentialRetrieval,

    /// <summary>
    ///     A new token was requested (cache miss or forced token refresh).
    /// </summary>
    FreshToken,

    /// <summary>
    ///     A still-valid token was returned from the in-memory cache.
    /// </summary>
    CachedToken
}