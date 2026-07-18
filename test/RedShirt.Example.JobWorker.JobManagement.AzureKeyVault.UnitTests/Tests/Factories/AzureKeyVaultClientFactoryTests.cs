using Azure.Core;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;
using System.Reflection;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.UnitTests.Tests.Factories;

public class AzureKeyVaultClientFactoryTests
{
    private static TokenCredential CreateFakeLocalTestingTokenCredential()
    {
        var credentialType = typeof(AzureKeyVaultClientFactory)
            .GetNestedType("FakeLocalTestingTokenCredential", BindingFlags.NonPublic);
        Assert.NotNull(credentialType);

        var credential = Activator.CreateInstance(credentialType) as TokenCredential;
        Assert.NotNull(credential);
        return credential;
    }

    private static void AssertAccessToken(AccessToken token, DateTimeOffset before)
    {
        Assert.False(string.IsNullOrWhiteSpace(token.Token));

        var parts = token.Token.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.All(parts, part => Assert.False(string.IsNullOrWhiteSpace(part)));

        Assert.True(token.ExpiresOn > before);
        Assert.True(token.ExpiresOn <= before.AddDays(1).AddMinutes(1));
    }

    [Fact]
    public async Task FakeLocalTestingTokenCredential_GetTokenAsync_ReturnsJwtShapedAccessToken()
    {
        var credential = CreateFakeLocalTestingTokenCredential();
        var before = DateTimeOffset.UtcNow;

        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://vault.azure.net/.default"]),
            TestContext.Current.CancellationToken);

        AssertAccessToken(token, before);
    }

    [Fact]
    public void FakeLocalTestingTokenCredential_GetToken_ReturnsJwtShapedAccessToken()
    {
        var credential = CreateFakeLocalTestingTokenCredential();
        var before = DateTimeOffset.UtcNow;

        var token = credential.GetToken(new TokenRequestContext(["https://vault.azure.net/.default"]),
            TestContext.Current.CancellationToken);

        AssertAccessToken(token, before);
    }

    [Fact]
    public void GetClient_LiveCredential()
    {
        var config = new AzureKeyVaultClientFactory.ConfigurationModel
        {
            KeyVaultUrl = "https://foo",
            GenerateLocalTestingToken = false
        };

        var factory = new AzureKeyVaultClientFactory(Options.Create(config));

        var client = factory.GetClient();
        Assert.NotNull(client);

        Assert.IsType<AzureKeyVaultClientWrapper>(client);
        var clientWrapperImplementation = (AzureKeyVaultClientWrapper) client;
        Assert.NotNull(clientWrapperImplementation.Client);
        Assert.Equal(new Uri("https://foo"), clientWrapperImplementation.Client.VaultUri);
    }

    [Fact]
    public void GetClient_LocalTestingToken()
    {
        var config = new AzureKeyVaultClientFactory.ConfigurationModel
        {
            KeyVaultUrl = "https://localhost:8443/",
            GenerateLocalTestingToken = true
        };

        var factory = new AzureKeyVaultClientFactory(Options.Create(config));

        var client = factory.GetClient();
        Assert.NotNull(client);

        Assert.IsType<AzureKeyVaultClientWrapper>(client);
        var clientWrapperImplementation = (AzureKeyVaultClientWrapper) client;
        Assert.NotNull(clientWrapperImplementation.Client);
        Assert.Equal(new Uri("https://localhost:8443/"), clientWrapperImplementation.Client.VaultUri);
    }
}