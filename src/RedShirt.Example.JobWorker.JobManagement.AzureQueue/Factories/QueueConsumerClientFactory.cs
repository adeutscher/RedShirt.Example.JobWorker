using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

internal interface IQueueConsumerClientFactory
{
    IQueueConsumerClientWrapper GetQueueClient();
}

internal class QueueConsumerClientFactory(IOptions<QueueConsumerClientFactory.ConfigurationModel> options)
    : IQueueConsumerClientFactory
{
    public IQueueConsumerClientWrapper GetQueueClient()
    {
        QueueClient innerClient;

        /*
         * Connection via Connection String and connection via Uri/TokenCredentials seem to be mutually exclusive.
         */

        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
        if (!string.IsNullOrWhiteSpace(options.Value.ConnectionString)
            && !string.IsNullOrWhiteSpace(options.Value.QueueName))
        {
            innerClient = new QueueClient(options.Value.ConnectionString, options.Value.QueueName);
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
        public required string? ConnectionString { get; init; }
        public required string? QueueName { get; init; }
    }
}