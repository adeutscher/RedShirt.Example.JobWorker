using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Azure.Extensions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureQueueStorageJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCommonAzureServices()
            // Required
            .AddSingleton<IJobSource, AzureQueueStorageJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<AzureQueueStorageConfigurationModel>(configuration.GetSection("JobSource:AzureQueueStorage"))
            .Configure<QueueConsumerClientFactory.ConfigurationModel>(
                configuration.GetSection("JobSource:AzureQueueStorage"))
            .AddSingleton<IQueueConsumerClientFactory, QueueConsumerClientFactory>()
            .AddSingleton<IQueueConsumerClientSource, QueueConsumerClientSource>()
            .AddSingleton<IAzureQueueStorageExceptionArbiterService, AzureQueueStorageExceptionArbiterService>()
            .AddSingleton<IAzureQueueStorageRetryWrapperService, AzureQueueStorageRetryWrapperService>()
            .AddSingleton<IAzureQueueStorageMessageSource, AzureQueueStorageMessageSource>();
    }
}