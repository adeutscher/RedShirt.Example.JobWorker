using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal interface INatsConnectionCacheSource
{
    Task<ClientCacheResponse<NatsConnectionBundle>> GetConnectionAsync(bool forceNewConnection = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class NatsConnectionCacheSource(INatsJetStreamContextFactory contextFactory) : INatsConnectionCacheSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private NatsConnectionBundle? _connection;

    public async Task<ClientCacheResponse<NatsConnectionBundle>> GetConnectionAsync(bool forceNewConnection = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewConnection && !forceNewSecretManagerPull && _connection is not null)
            {
                return new ClientCacheResponse<NatsConnectionBundle>
                {
                    CachedClient = true,
                    Client = _connection
                };
            }

            if (_connection is not null)
            {
                await _connection.Connection.DisposeAsync();
                _connection = null;
            }

            _connection = await contextFactory.CreateConnectionAsync(forceNewSecretManagerPull, cancellationToken);
            return new ClientCacheResponse<NatsConnectionBundle>
            {
                CachedClient = false,
                Client = _connection
            };
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}
