using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientSource
{
    Task<IServiceBusClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default);
}

internal class BusReceiverClientSource(IBusReceiverClientFactory factory) : IBusReceiverClientSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IServiceBusClientWrapper? _queueClient;

    public async Task<IServiceBusClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default)
    {
        if (_queueClient is not null)
        {
            return _queueClient;
        }

        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (_queueClient is not null)
            {
                return _queueClient;
            }

            _queueClient = await factory.GetQueueClientAsync(cancellationToken);
            return _queueClient;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}