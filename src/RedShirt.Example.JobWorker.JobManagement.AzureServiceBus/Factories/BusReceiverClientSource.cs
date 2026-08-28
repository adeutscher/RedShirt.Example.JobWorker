using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientSource
{
    Task<ClientCacheResponse<IServiceBusClientWrapper>> GetQueueClientAsync(bool forceNewClient = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class BusReceiverClientSource(IBusReceiverClientFactory factory) : IBusReceiverClientSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IServiceBusClientWrapper? _queueClient;

    public async Task<ClientCacheResponse<IServiceBusClientWrapper>> GetQueueClientAsync(bool forceNewClient = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewClient && !forceNewSecretManagerPull && _queueClient is not null)
            {
                return new ClientCacheResponse<IServiceBusClientWrapper>
                {
                    CachedClient = true,
                    Client = _queueClient
                };
            }

            _queueClient = await factory.GetQueueClientAsync(forceNewSecretManagerPull, cancellationToken);
            return new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = false,
                Client = _queueClient
            };
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}