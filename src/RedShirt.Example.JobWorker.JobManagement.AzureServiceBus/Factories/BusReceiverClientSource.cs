using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientSource
{
    IServiceBusClientWrapper GetQueueClient();
}

internal class BusReceiverClientSource(IBusReceiverClientFactory factory) : IBusReceiverClientSource
{
    private readonly Lazy<IServiceBusClientWrapper> _queueClient = new(factory.GetQueueClient);

    public IServiceBusClientWrapper GetQueueClient()
    {
        return _queueClient.Value;
    }
}