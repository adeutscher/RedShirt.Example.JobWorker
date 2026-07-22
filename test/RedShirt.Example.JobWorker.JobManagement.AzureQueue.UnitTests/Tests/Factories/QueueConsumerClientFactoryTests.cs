using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Services;
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

        var keyVaultService = new Mock<IAzureKeyVaultService>();
        keyVaultService
            .Setup(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken))
            .ReturnsAsync("bar");

        var factory = new QueueConsumerClientFactory(keyVaultService.Object, Options.Create(config));

        var client = factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        keyVaultService.Verify(c => c.GetSecretAsync(It.IsAny<string>(), TestContext.Current.CancellationToken),
            Times.Once);
        keyVaultService.Verify(c => c.GetSecretAsync("foo", TestContext.Current.CancellationToken), Times.Once);
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

        var keyVaultService = new Mock<IAzureKeyVaultService>();
        var factory = new QueueConsumerClientFactory(keyVaultService.Object, Options.Create(config));

        var client = factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        Assert.Empty(keyVaultService.Invocations);
    }
}