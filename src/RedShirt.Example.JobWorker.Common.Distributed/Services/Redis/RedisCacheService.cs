using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis.Resilience;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

internal class RedisCacheService(
    IDistributedRetryWrapperService retryWrapper,
    IRedisConnectionCacheService redisConnectionCacheService) : IRemoteCacheService
{
    private async Task<string?> GetStringInnerAsync(string key, CancellationToken cancellationToken)
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

    private async Task SetStringInnerAsync(string key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken)
    {
        var db = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);

        if (string.IsNullOrEmpty(value))
        {
            await db.StringGetDeleteAsync(key);
            return;
        }

        await db.StringSetAsync(key, value, expiry);
    }

    public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        return retryWrapper.RunAsync(ct => GetStringInnerAsync(key, ct), cancellationToken);
    }

    public Task SetStringAsync(string key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        return retryWrapper.RunAsync(ct => SetStringInnerAsync(key, value, expiry, ct), cancellationToken);
    }
}