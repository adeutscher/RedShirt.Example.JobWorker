using Azure;
using Azure.Security.KeyVault.Secrets;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.UnitTests.Tests;

public class AzureKeyVaultWrapperTests
{
    [Fact]
    public void Client_ExposesInjectedSecretClient()
    {
        var secretClient = new Mock<SecretClient>();
        var wrapper = new AzureKeyVaultClientWrapper(secretClient.Object);

        Assert.Same(secretClient.Object, wrapper.Client);
    }

    [Fact]
    public async Task GetSecretAsync_ForwardsSecretName()
    {
        const string secretName = "queue-connection";
        var secretClient = new Mock<SecretClient>();
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
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsSecretValue()
    {
        const string secretName = "connection-string";
        const string secretValue = "super-secret-value";
        var secretClient = new Mock<SecretClient>();
        secretClient
            .Setup(c => c.GetSecretAsync(secretName, null, null, TestContext.Current.CancellationToken))
            .ReturnsAsync(Response.FromValue(new KeyVaultSecret(secretName, secretValue), Mock.Of<Response>()));

        var wrapper = new AzureKeyVaultClientWrapper(secretClient.Object);

        var result = await wrapper.GetSecretAsync(secretName, TestContext.Current.CancellationToken);

        Assert.Equal(secretValue, result);
        secretClient.Verify(
            c => c.GetSecretAsync(secretName, null, null, TestContext.Current.CancellationToken),
            Times.Once);
    }
}