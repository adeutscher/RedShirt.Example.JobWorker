using Azure.Messaging.ServiceBus;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal interface IServiceBusMessageContainer
{
    ServiceBusReceivedMessage? Message { get; }
}