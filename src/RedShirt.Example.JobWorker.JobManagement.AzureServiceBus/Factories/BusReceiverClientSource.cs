using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;

internal interface IBusReceiverClientSource
{
    Task<ClientCacheResponse<IServiceBusProcessorWrapper>> GetProcessorAsync(bool forceNewClient = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);

    Task<ClientCacheResponse<IServiceBusClientWrapper>> GetQueueClientAsync(bool forceNewClient = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class BusReceiverClientSource(IBusReceiverClientFactory factory) : IBusReceiverClientSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IServiceBusProcessorWrapper? _processor;
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

    public async Task<ClientCacheResponse<IServiceBusProcessorWrapper>> GetProcessorAsync(bool forceNewClient = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewClient && !forceNewSecretManagerPull && _processor is not null)
            {
                return new ClientCacheResponse<IServiceBusProcessorWrapper>
                {
                    CachedClient = true,
                    Client = _processor
                };
            }

            if (_processor is not null)
            {
                await _processor.DisposeAsync();
                _processor = null;
            }

            _processor = await factory.GetProcessorAsync(forceNewSecretManagerPull, cancellationToken);
            return new ClientCacheResponse<IServiceBusProcessorWrapper>
            {
                CachedClient = false,
                Client = _processor
            };
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}