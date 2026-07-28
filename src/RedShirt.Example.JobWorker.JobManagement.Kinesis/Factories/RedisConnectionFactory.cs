using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Factories;

internal interface IRedisConnectionFactory
{
    Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default);
}

internal class RedisConnectionFactory(
    ISecretManagerCacheService secretManager,
    IOptions<RedisConnectionFactory.ConfigurationModel> options) : IRedisConnectionFactory
{
    public async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await ConnectionMultiplexer.ConnectAsync(
            await secretManager.GetSecretAsync(options.Value.ConnectionStringPath,
                cancellationToken: cancellationToken));
    }

    public sealed class ConfigurationModel
    {
        public required string ConnectionStringPath { get; init; }
    }
}