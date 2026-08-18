using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Docker.SecretManager.Services;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Docker.SecretManager.Extensions;

public static class ServiceCollectionExtensions
{
    internal const string ConfigurationSectionName = "Common:Secrets:Docker";

    public static IServiceCollection AddSecretManagerDocker(this IServiceCollection services,
        IConfiguration configuration)
    {
        return services
            .Configure<DockerSecretManagerService.ConfigurationModel>(
                configuration.GetSection(ConfigurationSectionName))
            .AddSingleton<ISecretManagerService, DockerSecretManagerService>();
    }
}