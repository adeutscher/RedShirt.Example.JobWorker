using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface IRedisConnectionSource
{
    IDatabase GetDatabase();
}

internal class RedisConnectionSource(IOptions<RedisConfiguration> options) : IRedisConnectionSource
{
    private readonly Lazy<ConnectionMultiplexer> _lazyConnection = new(() =>
        ConnectionMultiplexer.Connect($"{options.Value.EndpointAddress}:{options.Value.EndpointPort}"));

    public IDatabase GetDatabase()
    {
        return _lazyConnection.Value.GetDatabase();
    }
}