using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

internal interface IQueueConsumerClientFactory
{
    Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default);
}

internal class QueueConsumerClientFactory(
    ISecretManagerCacheService secretManagerCacheService,
    IAzureQueueStorageRetryWrapperService retryWrapperService,
    IOptions<QueueConsumerClientFactory.ConfigurationModel> options)
    : IQueueConsumerClientFactory
{
    private async Task<IQueueConsumerClientWrapper> GetQueueClientInnerAsync(CancellationToken cancellationToken)
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
                await secretManagerCacheService.GetSecretAsync(options.Value.ConnectionStringPath,
                    cancellationToken: cancellationToken);
            innerClient = new QueueClient(connectionString.Value, options.Value.QueueName);
        }
        else
        {
            innerClient = new QueueClient(new Uri(options.Value.Uri), new DefaultAzureCredential());
        }

        return new QueueClientWrapper(innerClient);
    }

    public Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(GetQueueClientInnerAsync, cancellationToken);
    }

    public sealed class ConfigurationModel
    {
        public required string Uri { get; init; }
        public required string? ConnectionStringPath { get; init; }
        public required string? QueueName { get; init; }
    }
}