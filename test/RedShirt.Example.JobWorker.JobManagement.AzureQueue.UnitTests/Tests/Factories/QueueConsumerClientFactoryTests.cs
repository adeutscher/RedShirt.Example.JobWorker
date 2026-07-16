using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Factories;

public class QueueConsumerClientFactoryTests
{
    [Fact]
    public void ShouldCreateQueueClient_ConnectionString()
    {
        var config = new QueueConsumerClientFactory.ConfigurationModel
        {
            Uri = string.Empty,
            ConnectionStringPath = "foo",
            QueueName = "bar"
        };

        var keyVaultClient = new Mock<IAzureKeyVaultClientWrapper>();
        var keyVaultClientSource = new Mock<IAzureKeyVaultClientSource>();
        keyVaultClientSource.Setup(s => s.GetKeyVaultClient())
            .Returns(keyVaultClient.Object);

        keyVaultClient
            .Setup(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken))
            .ReturnsAsync("bar");

        var factory = new QueueConsumerClientFactory(keyVaultClientSource.Object, Options.Create(config));

        var client = factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        keyVaultClientSource.Verify(s => s.GetKeyVaultClient(), Times.Once);
        keyVaultClient.Verify(c => c.GetSecretAsync(It.IsAny<string>(), TestContext.Current.CancellationToken),
            Times.Once);
        keyVaultClient.Verify(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public void ShouldCreateQueueClient_Uri()
    {
        var config = new QueueConsumerClientFactory.ConfigurationModel
        {
            Uri = "https://localhost:1234/",
            ConnectionStringPath = null,
            QueueName = null
        };

        var keyVaultClientSource = new Mock<IAzureKeyVaultClientSource>();
        var factory = new QueueConsumerClientFactory(keyVaultClientSource.Object, Options.Create(config));

        var client = factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        keyVaultClientSource.Verify(s => s.GetKeyVaultClient(), Times.Never);
    }
}