using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface IShortTermIteratorStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

internal class RedisShortTermIteratorStorage(IRedisConnectionCacheService redisConnectionCacheService)
    : IShortTermIteratorStorage
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var redis = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);

        var value = await redis.StringGetAsync(KeyHelper.GetCheckpointKey(key));
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToString();
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var db = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);

        if (string.IsNullOrEmpty(value))
        {
            await db.StringGetDeleteAsync(KeyHelper.GetCheckpointKey(key));
            return;
        }

        await db.StringSetAsync(KeyHelper.GetCheckpointKey(key), value,
            TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5));
    }
}