using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Factories;

public class QueueConsumerClientFactoryTests
{
    [Fact]
    public async Task ShouldCreateQueueClient_ConnectionString()
    {
        var config = new QueueConsumerClientFactory.ConfigurationModel
        {
            Uri = string.Empty,
            ConnectionStringPath = "foo",
            QueueName = "bar"
        };

        // Minimal connection string accepted by Azure.Storage.Queues QueueClient
        const string connectionString =
            "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;QueueEndpoint=https://127.0.0.1:10001/devstoreaccount1;";

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(c => c.GetSecretAsync("foo", null, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new SecretManagerCacheSecretResponse
            {
                Value = connectionString,
                QueriedSecretManager = true
            });

        var factory = new QueueConsumerClientFactory(secrets.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        secrets.Verify(c => c.GetSecretAsync("foo", null, false, TestContext.Current.CancellationToken), Times.Once);
        secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldCreateQueueClient_Uri()
    {
        var config = new QueueConsumerClientFactory.ConfigurationModel
        {
            Uri = "https://localhost:1234/",
            ConnectionStringPath = null,
            QueueName = null
        };

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        var factory = new QueueConsumerClientFactory(secrets.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);

        secrets.VerifyNoOtherCalls();
    }
}