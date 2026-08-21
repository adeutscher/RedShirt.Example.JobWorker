using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Distributed a single shared RabbitMQ IChannel.
/// </summary>
internal interface IRabbitMqConnectionCacheSource
{
    /// <param name="forceNewConnection">
    ///     When <c>true</c>, discard any cached connection and create a new one.
    /// </param>
    /// <param name="cancellationToken"></param>
    Task<IConnectionCacheResponse> GetConnectionAsync(bool forceNewConnection = false,
        CancellationToken cancellationToken = default);
}

internal class RabbitMqConnectionCacheSource(IRabbitMqConnectionFactory connectionFactory)
    : IRabbitMqConnectionCacheSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnectionCacheResponse> GetConnectionAsync(bool forceNewConnection = false,
        CancellationToken cancellationToken = default)
    {
        // Deliberately not returning the cached wrapper outside the semaphore lock
        // Recovery callbacks are sensitive to making sure that the most recent callback is present.

        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewConnection && _connection is not null)
            {
                return new ConnectionCacheResponse
                {
                    CachedConnection = true,
                    Connection = _connection
                };
            }

            _connection = await connectionFactory.GetConnectionAsync(cancellationToken);
            return new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = _connection
            };
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}