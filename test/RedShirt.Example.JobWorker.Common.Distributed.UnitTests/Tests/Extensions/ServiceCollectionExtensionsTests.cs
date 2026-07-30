using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Extensions;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;

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
}