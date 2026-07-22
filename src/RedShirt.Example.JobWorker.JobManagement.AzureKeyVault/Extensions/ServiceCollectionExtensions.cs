using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Services;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureKeyVaultSupportForJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .Configure<AzureKeyVaultClientFactory.ConfigurationModel>(
                configuration.GetSection("JobSource:AzureKeyVault"))
            .AddSingleton<IAzureKeyVaultClientFactory, AzureKeyVaultClientFactory>()
            .AddSingleton<IAzureKeyVaultClientSource, AzureKeyVaultClientSource>()
            .AddSingleton<IAzureKeyVaultService, AzureKeyVaultService>();
    }
}