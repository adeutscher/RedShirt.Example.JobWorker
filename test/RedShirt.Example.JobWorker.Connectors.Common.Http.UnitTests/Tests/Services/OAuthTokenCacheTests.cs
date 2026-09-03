using RedShirt.Example.JobWorker.Connectors.Common.Http.Enums;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Models;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Services;

namespace RedShirt.Example.JobWorker.Connectors.Common.Http.UnitTests.Tests.Services;

public class OAuthTokenCacheTests
{
    private static OAuthClientCredentialsRequest CreateRequest()
    {
        return new OAuthClientCredentialsRequest
        {
            TokenUrl = "https://auth.local/oauth/token",
            ClientIdPath = "/client/id",
            ClientSecretPath = "/client/secret",
            ScopeLabel = "audience",
            ScopeValue = "https://bar.local/api"
        };
    }

    [Fact]
    public async Task GetAsync_WhenCachedTokenExpired_RequestsFreshToken()
    {
        var request = CreateRequest();
        var tokenSource = new Mock<IOAuthTokenSource>(MockBehavior.Strict);
        var cache = new OAuthTokenCache(tokenSource.Object);
        var expiredToken = new OAuthTokenResponse
        {
            AccessToken = "expired-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        var freshToken = new OAuthTokenResponse
        {
            AccessToken = "fresh-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        tokenSource
            .SetupSequence(s => s.GetTokenAsync(request, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken)
            .ReturnsAsync(freshToken);

        await cache.GetAsync(request, false, false,
            TestContext.Current.CancellationToken);
        var response = await cache.GetAsync(request, false, false,
            TestContext.Current.CancellationToken);

        Assert.Equal("fresh-token", response.AccessToken);
        Assert.Equal(TokenCacheState.FreshToken, response.TokenCacheState);
    }

    [Fact]
    public async Task GetAsync_WhenCachedTokenIsValid_ReturnsCachedTokenWithoutCallingSource()
    {
        var request = CreateRequest();
        var tokenSource = new Mock<IOAuthTokenSource>(MockBehavior.Strict);
        var cache = new OAuthTokenCache(tokenSource.Object);
        var freshToken = new OAuthTokenResponse
        {
            AccessToken = "cached-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        tokenSource
            .Setup(s => s.GetTokenAsync(request, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(freshToken);

        await cache.GetAsync(request, false, false,
            TestContext.Current.CancellationToken);
        var response = await cache.GetAsync(request, false, false,
            TestContext.Current.CancellationToken);

        Assert.Equal("cached-token", response.AccessToken);
        Assert.Equal(TokenCacheState.CachedToken, response.TokenCacheState);
        tokenSource.Verify(
            s => s.GetTokenAsync(request, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenForceFreshCredentials_PassesForceToSourceAndReportsState()
    {
        var request = CreateRequest();
        var tokenSource = new Mock<IOAuthTokenSource>(MockBehavior.Strict);
        var cache = new OAuthTokenCache(tokenSource.Object);
        var token = new OAuthTokenResponse
        {
            AccessToken = "forced-credentials-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        tokenSource
            .Setup(s => s.GetTokenAsync(request, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var response = await cache.GetAsync(request, false, true,
            TestContext.Current.CancellationToken);

        Assert.Equal(TokenCacheState.ForcedCredentialRetrieval, response.TokenCacheState);
        tokenSource.Verify(
            s => s.GetTokenAsync(request, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenForceFreshToken_RequestsNewToken()
    {
        var request = CreateRequest();
        var tokenSource = new Mock<IOAuthTokenSource>(MockBehavior.Strict);
        var cache = new OAuthTokenCache(tokenSource.Object);
        var firstToken = new OAuthTokenResponse
        {
            AccessToken = "first-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        var secondToken = new OAuthTokenResponse
        {
            AccessToken = "second-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        tokenSource
            .SetupSequence(s => s.GetTokenAsync(request, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstToken)
            .ReturnsAsync(secondToken);

        await cache.GetAsync(request, false, false,
            TestContext.Current.CancellationToken);
        var response = await cache.GetAsync(request, true, false,
            TestContext.Current.CancellationToken);

        Assert.Equal("second-token", response.AccessToken);
        Assert.Equal(TokenCacheState.FreshToken, response.TokenCacheState);
    }
}