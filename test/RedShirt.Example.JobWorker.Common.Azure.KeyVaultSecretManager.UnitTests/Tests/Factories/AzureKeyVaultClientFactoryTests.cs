using Azure.Core;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using System.Reflection;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Factories;

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

    public class FakeLocalTestingTokenCredentialTests
    {
        [Fact]
        public async Task GetTokenAsync_ReturnsJwtShapedAccessToken()
        {
            var credential = CreateFakeLocalTestingTokenCredential();
            var before = DateTimeOffset.UtcNow;

            var token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://vault.azure.net/.default"]),
                TestContext.Current.CancellationToken);

            AssertAccessToken(token, before);
        }

        [Fact]
        public void GetToken_ReturnsJwtShapedAccessToken()
        {
            var credential = CreateFakeLocalTestingTokenCredential();
            var before = DateTimeOffset.UtcNow;

            var token = credential.GetToken(
                new TokenRequestContext(["https://vault.azure.net/.default"]),
                TestContext.Current.CancellationToken);

            AssertAccessToken(token, before);
        }
    }

    public class GetClient
    {
        [Fact]
        public void LiveCredential_ReturnsWrapperBoundToConfiguredVaultUri()
        {
            var config = new AzureKeyVaultClientFactory.ConfigurationModel
            {
                KeyVaultUrl = "https://foo",
                GenerateLocalTestingToken = false
            };

            var factory = new AzureKeyVaultClientFactory(Options.Create(config));

            var client = factory.GetClient();

            Assert.IsType<AzureKeyVaultClientWrapper>(client);
            var wrapper = (AzureKeyVaultClientWrapper) client;
            Assert.NotNull(wrapper.Client);
            Assert.Equal(new Uri("https://foo"), wrapper.Client.VaultUri);
        }

        [Fact]
        public void LocalTestingToken_ReturnsWrapperBoundToConfiguredVaultUri()
        {
            var config = new AzureKeyVaultClientFactory.ConfigurationModel
            {
                KeyVaultUrl = "https://localhost:8443/",
                GenerateLocalTestingToken = true
            };

            var factory = new AzureKeyVaultClientFactory(Options.Create(config));

            var client = factory.GetClient();

            Assert.IsType<AzureKeyVaultClientWrapper>(client);
            var wrapper = (AzureKeyVaultClientWrapper) client;
            Assert.NotNull(wrapper.Client);
            Assert.Equal(new Uri("https://localhost:8443/"), wrapper.Client.VaultUri);
        }
    }
}