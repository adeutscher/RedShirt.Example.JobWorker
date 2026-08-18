using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqSubscribeJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // Required
            .AddSingleton<IJobSource, RabbitMqSubscribeJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<RabbitMqSubscribeJobSource.ConfigurationModel>(configuration.GetSection("JobSource:RabbitMq"))
            .Configure<RabbitMqServerConfigurationSource.ConfigurationModel>(
                configuration.GetSection("JobSource:RabbitMq"))
            .AddSingleton<IRabbitMqServerConfigurationSource, RabbitMqServerConfigurationSource>()
            .AddSingleton<IInnerRabbitMqConnectionFactory, InnerRabbitMqConnectionFactory>()
            .AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>()
            .AddSingleton<IRabbitMqChannelCacheSource, RabbitMqChannelCacheSource>()
            .AddSingleton<IRabbitMqExceptionArbiterService, RabbitMqExceptionArbiterService>()
            .AddSingleton<IRabbitMqRetryWrapperService, RabbitMqRetryWrapperService>();
    }
}