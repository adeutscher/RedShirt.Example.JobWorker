using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Enums;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Models;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Services;
using System.Net;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;

internal interface IBarApiRequestHandlerRetryWrapperService
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Obtains Bar bearer tokens via <see cref="IOAuthTokenCache" /> and retries on unauthorized:
///     attempt 1 forces a fresh token (escalating to fresh credentials when inside the token refresh
///     cooldown); attempt 2 forces fresh credentials and a fresh token.
/// </summary>
internal sealed class BarApiRequestHandlerRetryWrapperService(
    IOAuthTokenCache oauthTokenCache,
    ILogger<BarApiRequestHandlerRetryWrapperService> logger,
    IOptions<BarApiRequestHandlerRetryWrapperService.ConfigurationModel> options)
    : IBarApiRequestHandlerRetryWrapperService
{
    private const int DefaultTokenRefreshCooldownSeconds = 60;

    private const string PreviousAttemptInvolvedEscalation = "e";

    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private HttpStatusCode? _previousAttemptStatusCode;

    private ResiliencePipeline? _retryPipeline;
    private DateTimeOffset? _tokenAttemptedAtUtc;
    private DateTimeOffset? _tokenFetchedAtUtc;

    private OAuthTokenCacheResponse? _tokenResult;

    private OAuthClientCredentialsRequest CreateOAuthRequest()
    {
        var configuration = options.Value;
        return new OAuthClientCredentialsRequest
        {
            TokenUrl = configuration.TokenUrl,
            ClientIdPath = configuration.ClientIdPath,
            ClientSecretPath = configuration.ClientSecretPath,
            ScopeLabel = configuration.ScopeLabel,
            ScopeValue = configuration.ScopeValue
        };
    }

    private bool IsWithinTokenRefreshCooldown()
    {
        if (_tokenFetchedAtUtc is not { } fetchedAtUtc)
        {
            return false;
        }

        return DateTimeOffset.UtcNow < fetchedAtUtc + options.Value.EffectiveTokenRefreshCooldown;
    }

    private bool IsAttemptWithinTokenRefreshCooldown()
    {
        if (_tokenAttemptedAtUtc is not { } attemptedAtUtc)
        {
            return false;
        }

        return DateTimeOffset.UtcNow < attemptedAtUtc + options.Value.EffectiveTokenRefreshCooldown;
    }

    private async Task<OAuthTokenCacheResponse> RefreshAndGetAccessTokenAsync(bool forceFreshToken,
        bool forceFreshCredentials,
        CancellationToken cancellationToken)
    {
        if (!forceFreshCredentials
            && _previousAttemptStatusCode != HttpStatusCode.OK
            && IsAttemptWithinTokenRefreshCooldown())
        {
            throw new BarTemporarilyUnavailableException();
        }

        _tokenAttemptedAtUtc = DateTimeOffset.UtcNow;
        OAuthTokenCacheResponse result;
        try
        {
            result = await oauthTokenCache.GetAsync(CreateOAuthRequest(), forceFreshToken, forceFreshCredentials,
                cancellationToken);
            _previousAttemptStatusCode = HttpStatusCode.OK;
        }
        catch (OAuthRequestException e)
        {
            _previousAttemptStatusCode = e.StatusCode;
            throw;
        }

        _tokenResult = result;
        _tokenFetchedAtUtc = DateTimeOffset.UtcNow;
        return result;
    }

    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                ShouldHandle = args =>
                {
                    // ReSharper disable once DuplicatedSequentialIfBodies
                    if (args is
                        {
                            AttemptNumber: 0, Outcome.Exception: OAuthRequestException
                            {
                                StatusCode: HttpStatusCode.Unauthorized,
                                CredentialStorageProblem: false,
                                FreshCredentialCacheResult: false
                            }
                        })
                    {
                        return PredicateResult.True();
                    }

                    if (args.Outcome.Exception is BarUnauthorizedException
                        && !IsWithinTokenRefreshCooldown()
                        && !(
                            args.Context.Properties.TryGetValue(
                                new ResiliencePropertyKey<bool>(PreviousAttemptInvolvedEscalation),
                                out var previousAttemptInvolvedEscalation)
                            && previousAttemptInvolvedEscalation
                        ))
                    {
                        return PredicateResult.True();
                    }

                    return PredicateResult.False();
                },
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    await _tokenGate.WaitAsync(args.Context.CancellationToken);
                    try
                    {
                        var forceFreshCredentials = args.AttemptNumber >= 1
                                                    || args.Outcome.Exception is OAuthRequestException;

                        var previousAccessToken = _tokenResult?.AccessToken;
                        logger.LogDebug(
                            "Refreshing Bar bearer token from {TokenUrl} (forceFreshCredentials: {ForceFreshCredentials})",
                            options.Value.TokenUrl, forceFreshCredentials);

                        OAuthTokenCacheResponse result;
                        try
                        {
                            result = await RefreshAndGetAccessTokenAsync(true, forceFreshCredentials,
                                args.Context.CancellationToken);
                        }
                        catch (OAuthRequestException) when (!forceFreshCredentials)
                        {
                            args.Context.Properties.Set(
                                new ResiliencePropertyKey<bool>(PreviousAttemptInvolvedEscalation), true);
                            result = await RefreshAndGetAccessTokenAsync(true, true, args.Context.CancellationToken);
                        }

                        if (forceFreshCredentials
                            && (string.Equals(previousAccessToken, result.AccessToken, StringComparison.Ordinal)
                                || result.TokenCacheState != TokenCacheState.ForcedCredentialRetrieval))
                        {
                            throw new BarUnauthorizedException();
                        }
                    }
                    finally
                    {
                        _tokenGate.Release();
                    }
                }
            })
            .Build();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenResult is not null)
        {
            return _tokenResult.AccessToken;
        }

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_tokenResult is not null)
            {
                return _tokenResult.AccessToken;
            }

            await RefreshAndGetAccessTokenAsync(false, false, cancellationToken);
            return _tokenResult!.AccessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken = default)
    {
        return GetRetryPipeline().ExecuteAsync(
            async token => await func(token),
            cancellationToken).AsTask();
    }

    internal sealed class ConfigurationModel
    {
        public required string TokenUrl { get; init; }

        public required string ClientIdPath { get; init; }

        public required string ClientSecretPath { get; init; }

        public required string? ScopeLabel { get; init; }

        public required string? ScopeValue { get; init; }

        public required int? TokenRefreshCooldownSeconds { get; init; }

        public TimeSpan EffectiveTokenRefreshCooldown =>
            TimeSpan.FromSeconds(Math.Max(1, TokenRefreshCooldownSeconds ?? DefaultTokenRefreshCooldownSeconds));
    }
}