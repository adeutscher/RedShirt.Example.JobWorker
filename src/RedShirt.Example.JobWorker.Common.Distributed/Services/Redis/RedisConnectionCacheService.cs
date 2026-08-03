using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

/// <summary>
///     Caching layer to keep multiple invokers/invocations using the same Redis connection.
/// </summary>
public interface IRedisConnectionCacheService
{
    Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
}

internal class RedisConnectionCacheService(IRedisConnectionFactory redisConnectionFactory)
    : IRedisConnectionCacheService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile IConnectionMultiplexer? _connectionMultiplexer;

    private void ThrowExceptionIfNotCurrentlyConnected()
    {
        if (_connectionMultiplexer?.IsConnected == false)
        {
            throw new WorkerDistributedException("Not currently connected", false, true);
        }
    }

    public async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = _connectionMultiplexer;
        if (existing is not null)
        {
            ThrowExceptionIfNotCurrentlyConnected();
            return existing.GetDatabase();
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Resharper was complaining about atomicity, but it's a wee bit moot when we're doing this in a semaphore lock. 
            // ReSharper disable once NonAtomicCompoundOperator
            _connectionMultiplexer ??= await redisConnectionFactory.GetConnectionAsync(cancellationToken);
            ThrowExceptionIfNotCurrentlyConnected();
            return _connectionMultiplexer.GetDatabase();
        }
        finally
        {
            _lock.Release();
        }
    }
}