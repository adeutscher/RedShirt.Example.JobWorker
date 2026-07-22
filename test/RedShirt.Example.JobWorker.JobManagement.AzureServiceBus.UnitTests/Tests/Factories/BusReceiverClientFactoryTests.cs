using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Services;
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

        var keyVaultService = new Mock<IAzureKeyVaultService>();
        keyVaultService
            .Setup(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken))
            // Using connection string suggestion from local testing's service bus emulator
            .ReturnsAsync(
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

        var factory = new BusReceiverClientFactory(keyVaultService.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.True(client is ServiceBusClientWrapper);
        var clientWrapperImplementation = client as ServiceBusClientWrapper;
        Assert.NotNull(clientWrapperImplementation);
        Assert.NotNull(clientWrapperImplementation.Client);

        keyVaultService.Verify(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken), Times.Once);
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

        var keyVaultService = new Mock<IAzureKeyVaultService>();

        var factory = new BusReceiverClientFactory(keyVaultService.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.True(client is ServiceBusClientWrapper);
        var clientWrapperImplementation = client as ServiceBusClientWrapper;
        Assert.NotNull(clientWrapperImplementation);
        Assert.NotNull(clientWrapperImplementation.Client);

        Assert.Empty(keyVaultService.Invocations);
    }
}