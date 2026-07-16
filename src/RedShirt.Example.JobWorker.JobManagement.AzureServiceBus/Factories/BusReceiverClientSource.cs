using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientSource
{
    Task<IServiceBusClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default);
}

internal class BusReceiverClientSource(IBusReceiverClientFactory factory) : IBusReceiverClientSource
{
    private readonly Lazy<Task<IServiceBusClientWrapper>> _queueClient = new(() => factory.GetQueueClientAsync());

    public Task<IServiceBusClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default)
    {
        return _queueClient.Value;
    }
}