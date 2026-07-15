using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureServiceBusJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // Required
            .AddSingleton<IJobSource, AzureServiceBusJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<AzureServiceBusConfigurationModel>(configuration.GetSection("JobSource:AzureServiceBus"))
            .Configure<BusReceiverClientFactory.ConfigurationModel>(
                configuration.GetSection("JobSource:AzureServiceBus"))
            .AddSingleton<IBusReceiverClientFactory, BusReceiverClientFactory>()
            .AddSingleton<IBusReceiverClientSource, BusReceiverClientSource>()
            .AddSingleton<IAzureServiceBusMessageSource, AzureServiceBusMessageSource>()
            .AddSingleton<IAzureServiceBusBodyStringRetriever, AzureServiceBusBodyStringRetriever>();
    }
}