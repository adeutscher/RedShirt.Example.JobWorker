using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class RedisCacheService(IRedisConnectionCacheService redisConnectionCacheService) : IRemoteCacheService
{
    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
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

    public async Task SetStringAsync(string? key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
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
}