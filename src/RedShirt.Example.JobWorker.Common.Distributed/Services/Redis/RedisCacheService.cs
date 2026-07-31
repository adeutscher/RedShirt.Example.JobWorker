using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class RedisCacheService(IRedisConnectionCacheService redisConnectionCacheService) : IRemoteCacheService
{
    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var redis = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);

            var value = await redis.StringGetAsync(key);
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.ToString();
        }
        catch (RedisConnectionException redisConnectionException)
        {
            throw new CacheConnectionException(redisConnectionException);
        }
        catch (TimeoutException timeoutException)
        {
            throw new CacheTimeoutException(timeoutException);
        }
    }

    public async Task SetStringAsync(string? key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);

            if (string.IsNullOrEmpty(value))
            {
                await db.StringGetDeleteAsync(key);
                return;
            }

            await db.StringSetAsync(key, value,
                expiry);
        }
        catch (RedisConnectionException redisConnectionException)
        {
            throw new CacheConnectionException(redisConnectionException);
        }
        catch (TimeoutException timeoutException)
        {
            throw new CacheTimeoutException(timeoutException);
        }
    }
}