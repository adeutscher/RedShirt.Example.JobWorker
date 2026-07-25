using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add common services and abstractions for pulling information from a secret manager.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddSecretManagerCore(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .Configure<SecretManagerCacheService.ConfigurationModel>(configuration.GetSection("Common:Secrets:Cache"))
            .AddSingleton<ISecretManagerCacheService, SecretManagerCacheService>();
    }
}