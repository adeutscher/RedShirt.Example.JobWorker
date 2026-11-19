using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Factories;

public class AzureKeyVaultClientFactoryTests
{
    public class FakeLocalTestingTokenCredentialTests
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
        private static string WriteTemporaryCaCertificate()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Test CA", key, HashAlgorithmName.SHA256);
            using var certificate =
                request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var path = Path.Combine(Path.GetTempPath(), $"kv-ca-{Guid.NewGuid()}.crt");
            File.WriteAllText(path, certificate.ExportCertificatePem());
            return path;
        }

        [Fact]
        public void LiveCredential_ReturnsWrapperBoundToConfiguredVaultUri()
        {
            var config = new AzureKeyVaultClientFactory.ConfigurationModel
            {
                KeyVaultUrl = "https://foo",
                GenerateLocalTestingToken = false
            };

            var factory = new AzureKeyVaultClientFactory(Options.Create(config),
                NullLogger<AzureKeyVaultClientFactory>.Instance);

            var client = factory.GetClient();

            Assert.IsType<AzureKeyVaultClientWrapper>(client);
            var wrapper = (AzureKeyVaultClientWrapper) client;
            Assert.NotNull(wrapper.Client);
            Assert.Equal(new Uri("https://foo"), wrapper.Client.VaultUri);
        }

        [Fact]
        public void LocalTestingToken_ReturnsWrapperBoundToConfiguredVaultUri()
        {
            var caPath = WriteTemporaryCaCertificate();
            try
            {
                var config = new AzureKeyVaultClientFactory.ConfigurationModel
                {
                    KeyVaultUrl = "https://localhost:8443/",
                    GenerateLocalTestingToken = true,
                    LocalTestingCaCertificatePath = caPath
                };

                var factory = new AzureKeyVaultClientFactory(Options.Create(config),
                    NullLogger<AzureKeyVaultClientFactory>.Instance);

                var client = factory.GetClient();

                Assert.IsType<AzureKeyVaultClientWrapper>(client);
                var wrapper = (AzureKeyVaultClientWrapper) client;
                Assert.NotNull(wrapper.Client);
                Assert.Equal(new Uri("https://localhost:8443/"), wrapper.Client.VaultUri);
            }
            finally
            {
                File.Delete(caPath);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void LocalTestingToken_WithoutCaCertificatePath_Throws(string? caPath)
        {
            var config = new AzureKeyVaultClientFactory.ConfigurationModel
            {
                KeyVaultUrl = "https://localhost:8443/",
                GenerateLocalTestingToken = true,
                LocalTestingCaCertificatePath = caPath
            };

            var factory = new AzureKeyVaultClientFactory(Options.Create(config),
                NullLogger<AzureKeyVaultClientFactory>.Instance);

            var thrown = Assert.Throws<InvalidOperationException>(() => factory.GetClient());
            Assert.Contains("LocalTestingCaCertificatePath", thrown.Message);
        }
    }
}