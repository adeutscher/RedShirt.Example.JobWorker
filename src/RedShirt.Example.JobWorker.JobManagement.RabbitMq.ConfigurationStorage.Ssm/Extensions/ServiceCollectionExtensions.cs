using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.ConfigurationStorage.Ssm.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.ConfigurationStorage.Ssm.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqConfigurationSsmStorage(this IServiceCollection services,
        IConfigurationRoot configurationRoot)
    {
        return services
            // Required
            .AddSingleton<IRabbitMqServerConfigurationSource, RabbitMqSsmConfigurationSource>()
            // Supporting
            .Configure<RabbitMqSsmConfigurationSource.ConfigurationModel>(
                configurationRoot.GetSection("JobSource:RabbitMq"))
            .AddAwsServiceWithLocalSupport<IAmazonSimpleSystemsManagement>();
    }
}