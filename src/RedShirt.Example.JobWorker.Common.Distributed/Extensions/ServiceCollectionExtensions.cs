using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using IRedisConnectionCacheService =
    RedShirt.Example.JobWorker.Common.Distributed.Services.Redis.IRedisConnectionCacheService;

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
            // Kind-of implementation-agnostic services.
            // The logic is mostly independent, but the use of the common ISafetyDisgraceStateService
            //  is based on the knowledge that the cache and lock systems are using the same underlying technology (Redis).
            .AddSingleton<ISafeRemoteCacheService, SafeRemoteCacheService>()
            .AddSingleton<ISafeAbstractedLockService, SafeAbstractedLockService>()
            .AddSingleton<ISafetyDisgraceStateService, SafetyDisgraceStateService>()
            .Configure<SafetyDisgraceStateService.ConfigurationModel>(
                configuration.GetSection("Common:Distributed:Safety"))
            // Redis-based
            .Configure<RedisConnectionFactory.ConfigurationModel>(configuration.GetSection("Common:Distributed:Redis"))
            .AddSingleton<IRedisConnectionFactory, RedisConnectionFactory>()
            .AddSingleton<IRedisConnectionCacheService, RedisConnectionCacheService>()
            .AddSingleton<IAbstractedLockService, RedisLockService>()
            .AddSingleton<IRemoteCacheService, RedisCacheService>();
    }
}