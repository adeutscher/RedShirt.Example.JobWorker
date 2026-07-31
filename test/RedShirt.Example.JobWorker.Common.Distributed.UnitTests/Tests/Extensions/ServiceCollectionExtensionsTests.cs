using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Extensions;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

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
                ["Common:Distributed:Redis:ConnectionStringPath"] = connectionStringPath
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
    public void AddDistributedServices_ConfiguresSafetyDisgraceStateService(int disgracePeriodSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Common:Distributed:Safety:DisgracePeriodSeconds"] = disgracePeriodSeconds.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddDistributedServices(configuration);

        using var provider = services.BuildServiceProvider();

        var safety = provider.GetRequiredService<IOptions<SafetyDisgraceStateService.ConfigurationModel>>().Value;
        Assert.Equal(disgracePeriodSeconds, safety.DisgracePeriodSeconds);
    }

    [Fact]
    public void AddDistributedServices_RegistersExpectedServiceAbstractions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Common:Distributed:Redis:ConnectionStringPath"] = "redis/connection-string",
                ["Common:Distributed:Safety:DisgracePeriodSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection()
            .AddDistributedServices(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(ISafeRemoteCacheService));
        Assert.Contains(services, d => d.ServiceType == typeof(ISafeAbstractedLockService));
        Assert.Contains(services, d => d.ServiceType == typeof(ISafetyDisgraceStateService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAbstractedLockService));
        Assert.Contains(services, d => d.ServiceType == typeof(IRemoteCacheService));
    }
}