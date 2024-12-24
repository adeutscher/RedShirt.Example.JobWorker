using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal interface IShortTermIteratorStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

internal class RedisShortTermIteratorStorage(IRedisConnectionSource redisConnectionSource) : IShortTermIteratorStorage
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var redis = redisConnectionSource.GetDatabase();

        var value = await redis.StringGetAsync(KeyHelper.GetCheckpointKey(key));
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToString();
    }

    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var db = redisConnectionSource.GetDatabase();
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (string.IsNullOrEmpty(value))
        {
            return db.StringGetDeleteAsync(KeyHelper.GetCheckpointKey(key));
        }

        return db.StringSetAsync(KeyHelper.GetCheckpointKey(key), value,
            TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5));
    }
}