using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.Distributed.Services;

namespace RedShirt.Example.JobWorker.Common.Distributed.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add common services for distributed caching and distributed locks.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddDistributedServices(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .Configure<RedisConnectionFactory.ConfigurationModel>(configuration.GetSection("JobSource:Kinesis:Redis"))
            .AddSingleton<IRedisConnectionFactory, RedisConnectionFactory>()
            .AddSingleton<IRedisConnectionCacheService, RedisConnectionCacheService>()
            .AddSingleton<IAbstractedLocker, RedisLocker>();
    }
}
