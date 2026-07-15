using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal interface IAzureServiceBusBodyStringRetriever
{
    string GetBody(IServiceBusMessageContainer input);
}

internal class AzureServiceBusBodyStringRetriever : IAzureServiceBusBodyStringRetriever
{
    public string GetBody(IServiceBusMessageContainer input)
    {
        // In hindsight, that was easy. I was kind of expecting a more painful process (see: template's BodyRetriever class for NATS)
        // That said, considering that ServiceBusReceivedMessage only has internal constructors, it's probably just as well...
        return input.Message.Body.ToString();
    }
}