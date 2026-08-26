using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ConfigurationSectionName = "JobSource:RabbitMq";

    public static IServiceCollection AddRabbitMqJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        var section = configuration.GetSection(ConfigurationSectionName);

        // shorthand
        var useSubscribe = section.Get<SubscribeConfigurationModel>()?.Subscribe == true;

        if (useSubscribe)
        {
            services.AddSingleton<IJobSource, RabbitMqSubscribeJobSource>();
        }
        else
        {
            services.AddSingleton<IJobSource, RabbitMqJobSource>();
        }

        return services
            // Required
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .AddSingleton<IRabbitMqSubscribeConfigurationService>(
                new RabbitMqSubscribeConfigurationService(useSubscribe))
            .Configure<RabbitMqQueueConfigurationModel>(section)
            .Configure<RabbitMqServerConfigurationSource.ConfigurationModel>(section)
            .AddSingleton<IRabbitMqServerConfigurationSource, RabbitMqServerConfigurationSource>()
            .AddSingleton<IInnerRabbitMqConnectionFactory, InnerRabbitMqConnectionFactory>()
            .AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>()
            .AddSingleton<IRabbitMqConnectionCacheSource, RabbitMqConnectionCacheSource>()
            .AddSingleton<IRabbitMqExceptionArbiterService, RabbitMqExceptionArbiterService>()
            .AddSingleton<IRabbitMqSubscribeExceptionArbiter, RabbitMqSubscribeExceptionArbiterService>()
            .AddSingleton<IRabbitMqRetryWrapperService, RabbitMqRetryWrapperService>()
            .AddSingleton<IRabbitMqChannelRetryWrapper, RabbitMqChannelRetryWrapper>();
    }

    private sealed class SubscribeConfigurationModel
    {
#pragma warning disable S3459
#pragma warning disable S1144
        public required bool Subscribe { get; init; }
#pragma warning disable S1144
#pragma warning restore S3459
    }
}