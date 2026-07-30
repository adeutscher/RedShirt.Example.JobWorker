using Azure;
using Azure.Security.KeyVault.Secrets;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Clients;

public class AzureKeyVaultClientWrapperTests
{
    [Fact]
    public void Client_ExposesInjectedSecretClient()
    {
        var secretClient = new Mock<SecretClient>();
        var wrapper = new AzureKeyVaultClientWrapper(secretClient.Object);

        Assert.Same(secretClient.Object, wrapper.Client);
    }

    public class GetSecretAsync
    {
        [Fact]
        public async Task ForwardsSecretNameAndCancellationToken()
        {
            const string secretName = "queue-connection";
            var secretClient = new Mock<SecretClient>(MockBehavior.Strict);
            secretClient
                .Setup(c => c.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SecretContentType?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, string? _, SecretContentType? _, CancellationToken _) =>
                    Response.FromValue(new KeyVaultSecret(name, $"value-for-{name}"), Mock.Of<Response>()));

            var wrapper = new AzureKeyVaultClientWrapper(secretClient.Object);

            var result = await wrapper.GetSecretAsync(secretName, TestContext.Current.CancellationToken);

            Assert.Equal($"value-for-{secretName}", result);
            secretClient.Verify(
                c => c.GetSecretAsync(secretName, null, null, TestContext.Current.CancellationToken),
                Times.Once);
            secretClient.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReturnsSecretValue()
        {
            const string secretName = "connection-string";
            const string secretValue = "super-secret-value";
            var secretClient = new Mock<SecretClient>(MockBehavior.Strict);
            secretClient
                .Setup(c => c.GetSecretAsync(secretName, null, null, TestContext.Current.CancellationToken))
                .ReturnsAsync(Response.FromValue(new KeyVaultSecret(secretName, secretValue), Mock.Of<Response>()));

            var wrapper = new AzureKeyVaultClientWrapper(secretClient.Object);

            var result = await wrapper.GetSecretAsync(secretName, TestContext.Current.CancellationToken);

            Assert.Equal(secretValue, result);
            secretClient.Verify(
                c => c.GetSecretAsync(secretName, null, null, TestContext.Current.CancellationToken),
                Times.Once);
            secretClient.VerifyNoOtherCalls();
        }
    }
}