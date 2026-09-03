using RedShirt.Example.JobWorker.Connectors.Common.Http.Enums;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Services;

/// <summary>
///     Get a token from an OAuth provider. Results are cached.
/// </summary>
public interface IOAuthTokenCache
{
    /// <summary>
    ///     Get a token from an OAuth provider.
    ///     Tokens are cached based on a checksum derived from request properties.
    /// </summary>
    /// <param name="request">
    ///     Token endpoint and secret-manager paths for client credentials.
    /// </param>
    /// <param name="forceFreshToken">
    ///     When <see langword="true" />, bypasses a still-valid cached token and requests a new one.
    /// </param>
    /// <param name="forceFreshCredentials">
    ///     When <see langword="true" />, force-refreshes client id/secret via the token source
    ///     (and therefore also requests a new token).
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation.
    /// </param>
    Task<OAuthTokenCacheResponse> GetAsync(OAuthClientCredentialsRequest request, bool forceFreshToken,
        bool forceFreshCredentials, CancellationToken cancellationToken = default);
}

/// <summary>
///     In-memory OAuth access-token cache keyed by a checksum of
///     <see cref="OAuthClientCredentialsRequest" /> identity fields.
/// </summary>
public sealed class OAuthTokenCache(IOAuthTokenSource tokenSource) : IOAuthTokenCache
{
    private readonly ConcurrentDictionary<string, OAuthTokenResponse> _cache = new();

    /// <summary>
    ///     Build a cache key derived from request parameters.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    private static string BuildCacheKeyChecksum(OAuthClientCredentialsRequest request)
    {
        var payload = string.Join('\n',
            request.TokenUrl,
            request.ClientIdPath,
            request.ClientSecretPath,
            request.ScopeLabel ?? string.Empty,
            request.ScopeValue ?? string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    /// <inheritdoc />
    public async Task<OAuthTokenCacheResponse> GetAsync(OAuthClientCredentialsRequest request,
        bool forceFreshToken, bool forceFreshCredentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cacheKeyChecksum = BuildCacheKeyChecksum(request);

        // A new token shall be provided if forceFreshToken is true, credentials must be refreshed,
        // or if the stored token is missing/expired.
        if (!forceFreshToken && !forceFreshCredentials
                             && _cache.TryGetValue(cacheKeyChecksum, out var cached)
                             && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return new OAuthTokenCacheResponse
            {
                AccessToken = cached.AccessToken,
                ExpiresAtUtc = cached.ExpiresAtUtc,
                TokenCacheState = TokenCacheState.CachedToken
            };
        }

        var token = await tokenSource.GetTokenAsync(request, forceFreshCredentials, cancellationToken);
        _cache[cacheKeyChecksum] = token;

        return new OAuthTokenCacheResponse
        {
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            TokenCacheState = forceFreshCredentials
                ? TokenCacheState.ForcedCredentialRetrieval
                : TokenCacheState.FreshToken
        };
    }
}