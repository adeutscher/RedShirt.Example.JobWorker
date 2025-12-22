using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureQueueStorageJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // Required
            .AddSingleton<IJobSource, AzureQueueStorageJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<AzureQueueStorageConfigurationModel>(configuration.GetSection("JobSource:AzureQueueStorage"))
            .Configure<QueueConsumerClientFactory.ConfigurationModel>(
                configuration.GetSection("JobSource:AzureQueueStorage"))
            .AddSingleton<IQueueConsumerClientFactory, QueueConsumerClientFactory>()
            .AddSingleton<IQueueConsumerClientSource, QueueConsumerClientSource>()
            .AddSingleton<IAzureQueueStorageMessageSource, AzureQueueStorageMessageSource>();
    }
}