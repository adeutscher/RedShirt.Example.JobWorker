using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // Required
            .AddSingleton<IJobSource, RabbitMqJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<RabbitMqJobSource.ConfigurationModel>(configuration.GetSection("JobSource:RabbitMq"))
            .Configure<RabbitMqServerConfigurationSource.ConfigurationModel>(
                configuration.GetSection("JobSource:RabbitMq"))
            .AddSingleton<IRabbitMqServerConfigurationSource, RabbitMqServerConfigurationSource>()
            .AddSingleton<IInnerRabbitMqConnectionFactory, InnerRabbitMqConnectionFactory>()
            .AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>()
            .AddSingleton<IRabbitMqExceptionArbiterService, RabbitMqExceptionArbiterService>()
            .AddSingleton<IRabbitMqRetryWrapperService, RabbitMqRetryWrapperService>();
    }
}