using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientFactory
{
    Task<IServiceBusProcessorWrapper> GetProcessorAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);

    Task<IServiceBusClientWrapper> GetQueueClientAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class BusReceiverClientFactory(
    ISecretManagerCacheService secretManagerService,
    ICoreConfigurationService coreConfigurationService,
    IOptions<BusReceiverClientFactory.ConfigurationModel> options) : IBusReceiverClientFactory
{
    private async Task<ServiceBusClient> CreateServiceBusClientAsync(bool forceNewSecretManagerPull,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.ConnectionStringPath))
        {
            var connectionString =
                await secretManagerService.GetSecretAsync(options.Value.ConnectionStringPath,
                    force: forceNewSecretManagerPull,
                    cancellationToken: cancellationToken);
            return new ServiceBusClient(connectionString.Value);
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (!string.IsNullOrWhiteSpace(options.Value.FullyQualifiedNamespace))
        {
            return new ServiceBusClient(options.Value.FullyQualifiedNamespace, new DefaultAzureCredential());
        }

        throw new InvalidOperationException("No service bus address has been set");
    }

    public async Task<IServiceBusClientWrapper> GetQueueClientAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var innerClient = await CreateServiceBusClientAsync(forceNewSecretManagerPull, cancellationToken);
        return new ServiceBusClientWrapper(innerClient.CreateReceiver(options.Value.QueueName));
    }

    public async Task<IServiceBusProcessorWrapper> GetProcessorAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var innerClient = await CreateServiceBusClientAsync(forceNewSecretManagerPull, cancellationToken);
        var processorOptions = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            PrefetchCount = Math.Max(1, coreConfigurationService.FetchCount),
            MaxConcurrentCalls = Math.Max(1, coreConfigurationService.FetchCount)
        };
        var processor = innerClient.CreateProcessor(options.Value.QueueName, processorOptions);
        return new ServiceBusProcessorWrapper(processor, innerClient);
    }

    public sealed class ConfigurationModel
    {
        public required string FullyQualifiedNamespace { get; init; }
        public required string? ConnectionStringPath { get; init; }
        public required string QueueName { get; init; }
    }
}