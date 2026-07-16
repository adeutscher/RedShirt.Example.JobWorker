using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Factories;

public class BusReceiverClientFactoryTests
{
    [Fact]
    public async Task ShouldCreateQueueClient_ConnectionString()
    {
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = "foo",
            QueueName = "bar",
            FullyQualifiedNamespace = "baz"
        };

        var keyVaultClient = new Mock<IAzureKeyVaultClientWrapper>();
        var keyVaultClientSource = new Mock<IAzureKeyVaultClientSource>();
        keyVaultClientSource.Setup(s => s.GetKeyVaultClient())
            .Returns(keyVaultClient.Object);

        keyVaultClient
            .Setup(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken))
            // Using connection string suggestion from local testing's service bus emulator
            .ReturnsAsync(
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

        var factory = new BusReceiverClientFactory(keyVaultClientSource.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.True(client is ServiceBusClientWrapper);
        var clientWrapperImplementation = client as ServiceBusClientWrapper;
        Assert.NotNull(clientWrapperImplementation);
        Assert.NotNull(clientWrapperImplementation.Client);

        keyVaultClientSource.Verify(s => s.GetKeyVaultClient(), Times.Once);
        keyVaultClient.Verify(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task ShouldCreateQueueClient_FullyQualifiedNamespace()
    {
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = null,
            QueueName = "foo",
            FullyQualifiedNamespace = "bar"
        };

        var keyVaultClientSource = new Mock<IAzureKeyVaultClientSource>();

        var factory = new BusReceiverClientFactory(keyVaultClientSource.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.True(client is ServiceBusClientWrapper);
        var clientWrapperImplementation = client as ServiceBusClientWrapper;
        Assert.NotNull(clientWrapperImplementation);
        Assert.NotNull(clientWrapperImplementation.Client);

        keyVaultClientSource.Verify(s => s.GetKeyVaultClient(), Times.Never);
    }
}