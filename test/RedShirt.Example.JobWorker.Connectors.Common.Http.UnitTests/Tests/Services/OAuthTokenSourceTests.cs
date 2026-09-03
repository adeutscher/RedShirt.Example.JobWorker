using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Models;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Services;
using RedShirt.Example.JobWorker.Connectors.Common.Http.UnitTests.Tests.Helpers;
using System.Net;
using System.Text;

namespace RedShirt.Example.JobWorker.Connectors.Common.Http.UnitTests.Tests.Services;

public class OAuthTokenSourceTests
{
    private const string ClientIdPath = "/client/id";
    private const string ClientSecretPath = "/client/secret";

    private static OAuthClientCredentialsRequest CreateRequest()
    {
        return new OAuthClientCredentialsRequest
        {
            TokenUrl = "https://auth.local/oauth/token",
            ClientIdPath = ClientIdPath,
            ClientSecretPath = ClientSecretPath,
            ScopeLabel = null,
            ScopeValue = null
        };
    }

    private static OAuthTokenSource CreateSut(
        StubHttpMessageHandler handler,
        Mock<ISecretManagerCacheService> secretManager,
        int? fallbackJwtExpiryMinutes = null)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory
            .Setup(f => f.CreateClient(nameof(OAuthTokenSource)))
            .Returns(new HttpClient(handler));

        return new OAuthTokenSource(
            httpClientFactory.Object,
            secretManager.Object,
            NullLogger<OAuthTokenSource>.Instance,
            Options.Create(new OAuthTokenSource.ConfigurationModel
            {
                FallbackJwtExpiryTimeMinutes = fallbackJwtExpiryMinutes
            }));
    }

    private static void SetupSecrets(Mock<ISecretManagerCacheService> secretManager, bool queriedSecretManager = false)
    {
        secretManager
            .Setup(s => s.GetSecretsAsync(
                It.Is<List<string>>(paths => paths.Contains(ClientIdPath) && paths.Contains(ClientSecretPath)),
                null,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretManagerCacheSecretsResponse
            {
                Values = new Dictionary<string, string>
                {
                    [ClientIdPath] = "client-id",
                    [ClientSecretPath] = "client-secret"
                },
                QueriedSecretManager = queriedSecretManager
            });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(20, 20)]
    public void ConfigurationModel_EffectiveFallbackJwtExpiryTimeMinutes(int configured, int expectedMinutes)
    {
        var model = new OAuthTokenSource.ConfigurationModel {FallbackJwtExpiryTimeMinutes = configured};

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), model.EffectiveFallbackJwtExpiryTimeMinutes);
    }

    [Fact]
    public async Task GetTokenAsync_WhenExpiresInMissing_UsesConfiguredFallbackLifetime()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"access-token\"}", Encoding.UTF8, "application/json")
        });
        var secretManager = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        SetupSecrets(secretManager);
        var sut = CreateSut(handler, secretManager, 10);
        var before = DateTimeOffset.UtcNow;

        var response =
            await sut.GetTokenAsync(CreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.InRange(response.ExpiresAtUtc, before.AddMinutes(9), before.AddMinutes(11));
    }

    [Fact]
    public async Task GetTokenAsync_WhenResponseIsInvalidJson_ThrowsOAuthRequestJsonException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        var secretManager = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        SetupSecrets(secretManager);
        var sut = CreateSut(handler, secretManager);

        await Assert.ThrowsAsync<OAuthRequestJsonException>(() =>
            sut.GetTokenAsync(CreateRequest(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTokenAsync_WhenSecretManagerFails_ThrowsOAuthRequestExceptionWithCredentialStorageProblem()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not call http"));
        var secretManager = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secretManager
            .Setup(s => s.GetSecretsAsync(
                It.IsAny<List<string>>(),
                null,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerSecretManagerException("secret store unavailable")
            {
                IsHandled = false,
                CouldBeTransient = true,
                CouldBeExternallySolvable = true
            });
        var sut = CreateSut(handler, secretManager);

        var thrown = await Assert.ThrowsAsync<OAuthRequestException>(() =>
            sut.GetTokenAsync(CreateRequest(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(thrown.CredentialStorageProblem);
        Assert.Null(thrown.StatusCode);
        Assert.True(thrown.FreshCredentialCacheResult);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenEndpointReturnsNonSuccess_ThrowsOAuthRequestException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var secretManager = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        SetupSecrets(secretManager, true);
        var sut = CreateSut(handler, secretManager);

        var thrown = await Assert.ThrowsAsync<OAuthRequestException>(() =>
            sut.GetTokenAsync(CreateRequest(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, thrown.StatusCode);
        Assert.False(thrown.CredentialStorageProblem);
        Assert.True(thrown.FreshCredentialCacheResult);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenEndpointReturnsSuccess_UsesExpiresIn()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"access-token\",\"expires_in\":120}",
                Encoding.UTF8,
                "application/json")
        });
        var secretManager = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        SetupSecrets(secretManager);
        var sut = CreateSut(handler, secretManager);
        var before = DateTimeOffset.UtcNow;

        var response =
            await sut.GetTokenAsync(CreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("access-token", response.AccessToken);
        Assert.InRange(response.ExpiresAtUtc, before.AddSeconds(119), before.AddSeconds(121));
    }
}