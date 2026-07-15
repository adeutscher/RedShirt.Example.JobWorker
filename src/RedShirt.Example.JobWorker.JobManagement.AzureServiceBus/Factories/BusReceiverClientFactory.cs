using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientFactory
{
    IServiceBusClientWrapper GetQueueClient();
}

internal class BusReceiverClientFactory(IOptions<BusReceiverClientFactory.ConfigurationModel> options)
    : IBusReceiverClientFactory
{
    public IServiceBusClientWrapper GetQueueClient()
    {
        ServiceBusClient innerClient;

        /*
         * Connection via Connection String and connection via Uri/Namespace seems to be mutually exclusive.
         */

        if (!string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            innerClient = new ServiceBusClient(options.Value.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(options.Value.FullyQualifiedNamespace))
        {
            innerClient = new ServiceBusClient(options.Value.FullyQualifiedNamespace, new DefaultAzureCredential());
        }
        else
        {
            throw new ServiceBusSourceException("No service bus address has been set");
        }

        return new ServiceBusClientWrapper(innerClient.CreateReceiver(options.Value.QueueName));
    }

    public sealed class ConfigurationModel
    {
        public required string FullyQualifiedNamespace { get; init; }
        public required string? ConnectionString { get; init; }
        public required string QueueName { get; init; }
    }
}