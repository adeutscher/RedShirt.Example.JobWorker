using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using IRedisConnectionCacheService =
    RedShirt.Example.JobWorker.Common.Distributed.Services.IRedisConnectionCacheService;

namespace RedShirt.Example.JobWorker.Common.Distributed.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add common services for caching and locks in support of the distributed execution of multiple instances of the
    ///     worker process.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddDistributedServices(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // Implementation-agnostic services
            .Configure<SafeRemoteCacheService.ConfigurationModel>(configuration.GetSection("Common:Cache:SafeCache"))
            .AddSingleton<ISafeRemoteCacheService, SafeRemoteCacheService>()
            // Redis-based
            .Configure<RedisConnectionFactory.ConfigurationModel>(configuration.GetSection("Common:Cache:Redis"))
            .AddSingleton<IRedisConnectionFactory, RedisConnectionFactory>()
            .AddSingleton<IRedisConnectionCacheService, RedisConnectionCacheService>()
            .AddSingleton<IAbstractedLockService, RedisLockService>()
            .AddSingleton<IRemoteCacheService, RedisCacheService>();
    }
}