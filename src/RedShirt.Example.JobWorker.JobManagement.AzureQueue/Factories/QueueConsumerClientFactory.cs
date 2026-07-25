using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

internal interface IQueueConsumerClientFactory
{
    Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default);
}

internal class QueueConsumerClientFactory(
    ISecretManagerCacheService secretManagerCacheService,
    IOptions<QueueConsumerClientFactory.ConfigurationModel> options)
    : IQueueConsumerClientFactory
{
    public async Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default)
    {
        QueueClient innerClient;

        /*
         * Connection via Connection String and connection via Uri/TokenCredentials seem to be mutually exclusive.
         */

        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
        if (!string.IsNullOrWhiteSpace(options.Value.ConnectionStringPath)
            && !string.IsNullOrWhiteSpace(options.Value.QueueName))
        {
            var connectionString =
                await secretManagerCacheService.GetSecretAsync(options.Value.ConnectionStringPath, cancellationToken: cancellationToken);
            innerClient = new QueueClient(connectionString, options.Value.QueueName);
        }
        else
        {
            innerClient = new QueueClient(new Uri(options.Value.Uri), new DefaultAzureCredential());
        }

        return new QueueClientWrapper(innerClient);
    }

    public sealed class ConfigurationModel
    {
        public required string Uri { get; init; }
        public required string? ConnectionStringPath { get; init; }
        public required string? QueueName { get; init; }
    }
}