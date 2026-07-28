using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSingleton<IJobSource, KafkaJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            .Configure<KafkaConsumerFactory.ConfigurationModel>(configuration.GetSection("JobSource:Kafka"))
            .AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>()
            .AddSingleton<IKafkaConsumerSource, KafkaConsumerSource>()
            .AddSingleton<IKafkaMessageSource, KafkaMessageSource>();
    }
}