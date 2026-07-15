using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Factories;

public class BusReceiverClientFactoryTests
{
    [Fact]
    public void ShouldCreateQueueClient_FullyQualifiedNamespace()
    {
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionString = null,
            QueueName = "foo",
            FullyQualifiedNamespace = "bar"
        };

        var factory = new BusReceiverClientFactory(Options.Create(config));

        var client = factory.GetQueueClient();
        Assert.NotNull(client);

        Assert.True(client is ServiceBusClientWrapper);
        var clientWrapperImplementation = client as ServiceBusClientWrapper;
        Assert.NotNull(clientWrapperImplementation);
        Assert.NotNull(clientWrapperImplementation.Client);
    }
}