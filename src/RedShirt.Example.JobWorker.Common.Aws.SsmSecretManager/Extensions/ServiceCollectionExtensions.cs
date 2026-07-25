using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add common services and abstractions for pulling information from a secret manager.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddSecretManagerSsm(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddAwsServiceWithLocalSupport<IAmazonSimpleSystemsManagement>()
            .AddSingleton<ISecretManagerService, SsmSecretManagerService>();
    }
}