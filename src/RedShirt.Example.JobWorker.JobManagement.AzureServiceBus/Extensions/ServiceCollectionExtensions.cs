using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Azure.Extensions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ConfigurationSectionName = "JobSource:AzureServiceBus";

    public static IServiceCollection AddAzureServiceBusJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        var section = configuration.GetSection(ConfigurationSectionName);

        return services
            .AddSingleton<IJobSource, AzureServiceBusJobSource>()
            .AddCommonAzureServices()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            .Configure<AzureServiceBusConfigurationModel>(section)
            .Configure<BusReceiverClientFactory.ConfigurationModel>(section)
            .AddSingleton<IBusReceiverClientFactory, BusReceiverClientFactory>()
            .AddSingleton<IBusReceiverClientSource, BusReceiverClientSource>()
            .AddSingleton<IAzureServiceBusExceptionArbiterService, AzureServiceBusExceptionArbiterService>()
            .AddSingleton<IAzureServiceBusDetailedExceptionArbiter, AzureServiceBusDetailedExceptionArbiterService>()
            .AddSingleton<IAzureServiceBusRetryWrapperService, AzureServiceBusRetryWrapperService>()
            .AddSingleton<IAzureServiceBusClientRetryWrapper, AzureServiceBusClientRetryWrapper>()
            .AddSingleton<IAzureServiceBusMessageSource, AzureServiceBusMessageSource>();
    }
}