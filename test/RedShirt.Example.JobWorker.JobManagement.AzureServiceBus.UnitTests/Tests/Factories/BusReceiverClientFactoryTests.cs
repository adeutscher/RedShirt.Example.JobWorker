using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
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

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(c => c.GetSecretAsync("foo", null, false, TestContext.Current.CancellationToken))
            // Using connection string suggestion from local testing's service bus emulator
            .ReturnsAsync(
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.IsType<ServiceBusClientWrapper>(client);
        var clientWrapperImplementation = (ServiceBusClientWrapper) client;
        Assert.NotNull(clientWrapperImplementation.Client);

        secrets.Verify(c => c.GetSecretAsync("foo", null, false, TestContext.Current.CancellationToken), Times.Once);
        secrets.VerifyNoOtherCalls();
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

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);

        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.IsType<ServiceBusClientWrapper>(client);
        var clientWrapperImplementation = (ServiceBusClientWrapper) client;
        Assert.NotNull(clientWrapperImplementation.Client);

        secrets.VerifyNoOtherCalls();
    }
}