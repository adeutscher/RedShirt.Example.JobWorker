using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Factories;

public class BusReceiverClientFactoryTests
{
    [Fact]
    public async Task GetQueueClientAsync_PassesCancellationTokenToSecretManager()
    {
        const string secretPath = "secrets/service-bus-ct";
        using var cts = new CancellationTokenSource();

        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = secretPath,
            QueueName = "queue",
            FullyQualifiedNamespace = "unused"
        };

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(c => c.GetSecretAsync(secretPath, null, false, cts.Token))
            .ReturnsAsync(new SecretManagerCacheSecretResponse
            {
                Value =
                    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
                QueriedSecretManager = true
            });

        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        await factory.GetQueueClientAsync(cts.Token);

        secrets.Verify(c => c.GetSecretAsync(secretPath, null, false, cts.Token), Times.Once);
        secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQueueClientAsync_PrefersConnectionStringPathOverNamespace()
    {
        const string secretPath = "secrets/service-bus";
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = secretPath,
            QueueName = "preferred-queue",
            FullyQualifiedNamespace = "should-not-be-used.servicebus.windows.net"
        };

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(c => c.GetSecretAsync(secretPath, null, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new SecretManagerCacheSecretResponse
            {
                Value =
                    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
                QueriedSecretManager = true
            });

        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);

        Assert.IsType<ServiceBusClientWrapper>(client);
        secrets.Verify(c => c.GetSecretAsync(secretPath, null, false, TestContext.Current.CancellationToken),
            Times.Once);
        secrets.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    [InlineData("", "")]
    [InlineData("", "   ")]
    [InlineData("   ", "")]
    [InlineData("   ", "   ")]
    public async Task GetQueueClientAsync_ThrowsWhenConnectionStringPathAndNamespaceAreMissing(
        string? connectionStringPath,
        string? fullyQualifiedNamespace)
    {
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = connectionStringPath,
            QueueName = "queue",
            FullyQualifiedNamespace = fullyQualifiedNamespace!
        };

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.GetQueueClientAsync(TestContext.Current.CancellationToken));

        secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQueueClientAsync_ThrowsWhenNoAddressConfigured()
    {
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = null,
            QueueName = "queue",
            FullyQualifiedNamespace = ""
        };

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.GetQueueClientAsync(TestContext.Current.CancellationToken));

        Assert.Equal("No service bus address has been set", exception.Message);
        secrets.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetQueueClientAsync_UsesNamespaceWhenConnectionStringPathIsBlank(string? connectionStringPath)
    {
        var config = new BusReceiverClientFactory.ConfigurationModel
        {
            ConnectionStringPath = connectionStringPath,
            QueueName = "queue-name",
            FullyQualifiedNamespace = "namespace.servicebus.windows.net"
        };

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        var factory = new BusReceiverClientFactory(secrets.Object, Options.Create(config));

        var client = await factory.GetQueueClientAsync(TestContext.Current.CancellationToken);

        Assert.IsType<ServiceBusClientWrapper>(client);
        Assert.NotNull(((ServiceBusClientWrapper) client).Client);
        secrets.VerifyNoOtherCalls();
    }

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
            .ReturnsAsync(new SecretManagerCacheSecretResponse
            {
                Value =
                    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
                QueriedSecretManager = true
            });

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