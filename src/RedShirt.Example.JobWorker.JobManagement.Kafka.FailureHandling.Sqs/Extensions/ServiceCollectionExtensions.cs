using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Extensions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaSqsFailureHandling(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSqs()
            .AddSingleton<IJobFailureHandler, SqsQueueFailureHandler>()
            .Configure<SqsQueueFailureHandler.ConfigurationModel>(
                configuration.GetSection("JobSource:Kafka:Failures"));
    }
}