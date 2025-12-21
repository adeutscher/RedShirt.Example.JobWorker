using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Factories;

public class QueueConsumerClientFactoryTests
{
    [Fact]
    public void ShouldCreateQueueClient_Uri()
    {
        var config = new QueueConsumerClientFactory.ConfigurationModel
        {
            Uri = "https://localhost:1234/",
            ConnectionString = null,
            QueueName = null
        };

        var factory = new QueueConsumerClientFactory(Options.Create(config));

        var client = factory.GetQueueClient();
        Assert.NotNull(client);
    }
}