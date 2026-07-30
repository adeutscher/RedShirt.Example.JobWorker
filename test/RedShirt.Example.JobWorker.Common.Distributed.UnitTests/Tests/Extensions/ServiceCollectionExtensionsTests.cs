using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Extensions;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.Distributed.Services;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData("redis/connection-string")]
    [InlineData("secrets/redis")]
    public void AddDistributedServices_ConfiguresRedisConnectionFactory(string connectionStringPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kinesis:Redis:ConnectionStringPath"] = connectionStringPath
            })
            .Build();

        var services = new ServiceCollection()
            .AddDistributedServices(configuration);

        using var provider = services.BuildServiceProvider();

        var redis = provider.GetRequiredService<IOptions<RedisConnectionFactory.ConfigurationModel>>().Value;
        Assert.Equal(connectionStringPath, redis.ConnectionStringPath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(60)]
    public void AddDistributedServices_ConfiguresSafeRemoteCacheService(int disgracePeriodSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Common:Cache:SafeCache:DisgracePeriodSeconds"] = disgracePeriodSeconds.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddDistributedServices(configuration);

        using var provider = services.BuildServiceProvider();

        var safeCache = provider.GetRequiredService<IOptions<SafeRemoteCacheService.ConfigurationModel>>().Value;
        Assert.Equal(disgracePeriodSeconds, safeCache.DisgracePeriodSeconds);
    }
}