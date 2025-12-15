using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.ConfigurationStorage.Ssm.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.ConfigurationStorage.Ssm.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActiveMqConfigurationSsmStorage(this IServiceCollection services,
        IConfigurationRoot configurationRoot)
    {
        return services
            // Required
            .AddSingleton<IActiveMqServerConfigurationSource, ActiveMqSsmConfigurationSource>()
            // Supporting
            .Configure<ActiveMqSsmConfigurationSource.ConfigurationModel>(
                configurationRoot.GetSection("JobSource:ActiveMq"))
            .AddAwsServiceWithLocalSupport<IAmazonSimpleSystemsManagement>();
    }
}