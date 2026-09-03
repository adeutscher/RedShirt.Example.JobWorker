using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Models;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Services;

/// <summary>
///     Obtains OAuth 2.0 access tokens via the client-credentials grant.
/// </summary>
public interface IOAuthTokenSource
{
    /// <summary>
    ///     Requests an access token using client credentials resolved from the secret manager.
    /// </summary>
    /// <param name="request">
    ///     Token endpoint and secret-manager paths for client credentials.
    /// </param>
    /// <param name="force">
    ///     When <see langword="true" />, force-refreshes client id/secret from the secret manager
    ///     (subject to the secret cache force-cooldown).
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation.
    /// </param>
    /// <returns>
    ///     The access token and computed expiry.
    /// </returns>
    Task<OAuthTokenResponse> GetTokenAsync(OAuthClientCredentialsRequest request, bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     OAuth 2.0 client-credentials token source. Resolves client id/secret via
///     <see cref="ISecretManagerCacheService" /> and posts a form-urlencoded token request.
/// </summary>
public sealed class OAuthTokenSource(
    IHttpClientFactory httpClientFactory,
    ISecretManagerCacheService secretManager,
    ILogger<OAuthTokenSource> logger,
    IOptions<OAuthTokenSource.ConfigurationModel> options) : IOAuthTokenSource
{
    private const int DefaultFallbackJwtExpiryTimeMinutes = 30;

    public async Task<OAuthTokenResponse> GetTokenAsync(OAuthClientCredentialsRequest request, bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogTrace("Requesting OAuth client-credentials token from {TokenUrl} (force: {Force})",
            request.TokenUrl, force);

        SecretManagerCacheSecretsResponse secretsResponse;
        try
        {
            secretsResponse = await secretManager.GetSecretsAsync(
                [request.ClientIdPath, request.ClientSecretPath],
                force: force,
                cancellationToken: cancellationToken);
        }
        catch (WorkerSecretManagerException e)
        {
            throw new OAuthRequestException(e.Message, e)
            {
                StatusCode = null,
                CredentialStorageProblem = true,
                // Assume that cache layer is working correctly and that the underlying secret manager behind the cache is misbehaving
                FreshCredentialCacheResult = true
            };
        }

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = secretsResponse.Values[request.ClientIdPath],
            ["client_secret"] = secretsResponse.Values[request.ClientSecretPath]
        };

        if (!string.IsNullOrWhiteSpace(request.ScopeLabel) && !string.IsNullOrWhiteSpace(request.ScopeValue))
        {
            parameters[request.ScopeLabel] = request.ScopeValue;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.TokenUrl);
        httpRequest.Content = new FormUrlEncodedContent(parameters);

        using var client = httpClientFactory.CreateClient(nameof(OAuthTokenSource));
        using var response = await client.SendAsync(httpRequest, cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            logger.LogError(
                "Failed to obtain OAuth token from {TokenUrl}: {StatusCodeValue} ({StatusCode})",
                request.TokenUrl, (int) response.StatusCode, response.StatusCode);
            throw new OAuthRequestException(
                $"Response status code does not indicate success: {(int) response.StatusCode} ({response.StatusCode}).")
            {
                StatusCode = response.StatusCode,
                CredentialStorageProblem = false,
                FreshCredentialCacheResult = secretsResponse.QueriedSecretManager
            };
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        TokenEndpointResponse? responseObject;
        try
        {
            responseObject = JsonSerializer.Deserialize<TokenEndpointResponse>(content);
        }
        catch (JsonException exception)
        {
            throw new OAuthRequestJsonException("OAuth token response could not be deserialized.", exception);
        }

        if (string.IsNullOrWhiteSpace(responseObject?.AccessToken))
        {
            throw new OAuthRequestJsonException("OAuth token response did not contain an access_token.");
        }

        var issuedAtUtc = DateTimeOffset.UtcNow;
        var expiresAtUtc = responseObject.ExpiresIn is > 0
            ? issuedAtUtc.AddSeconds(responseObject.ExpiresIn.Value)
            : issuedAtUtc.Add(options.Value.EffectiveFallbackJwtExpiryTimeMinutes);

        return new OAuthTokenResponse
        {
            AccessToken = responseObject.AccessToken,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Minutes to treat an access token as valid when the token response omits or has an invalid
        ///     <c>expires_in</c>. When null, <see cref="DefaultFallbackJwtExpiryTimeMinutes" /> is used.
        /// </summary>
        public required int? FallbackJwtExpiryTimeMinutes { get; init; }

        /// <summary>
        ///     Effective fallback lifetime for tokens without a usable <c>expires_in</c>.
        /// </summary>
        public TimeSpan EffectiveFallbackJwtExpiryTimeMinutes =>
            TimeSpan.FromMinutes(Math.Max(1, FallbackJwtExpiryTimeMinutes ?? DefaultFallbackJwtExpiryTimeMinutes));
    }

    private sealed class TokenEndpointResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }
}