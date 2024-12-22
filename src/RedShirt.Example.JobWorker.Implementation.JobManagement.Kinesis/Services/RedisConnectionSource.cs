using Microsoft.Extensions.Options;
using RedLockNet;
using RedLockNet.SERedis;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal interface IRedisConnectionSource
{
    IDatabase GetDatabase();
    IDistributedLockFactory GetLockFactory();
}

internal class RedisConnectionSource(IOptions<RedisConfiguration> options) : IRedisConnectionSource
{
    private readonly Lazy<ConnectionMultiplexer> _lazyConnection = new(() =>
        ConnectionMultiplexer.Connect($"{options.Value.EndpointAddress}:{options.Value.EndpointPort}"));

    public IDatabase GetDatabase()
    {
        return _lazyConnection.Value.GetDatabase();
    }

    public IDistributedLockFactory GetLockFactory()
    {
        return RedLockFactory.Create([_lazyConnection.Value]);
    }
}