using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ConfigurationSectionName = "JobSource:ActiveMq";

    public static IServiceCollection AddActiveMqJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        var section = configuration.GetSection(ConfigurationSectionName);

        // shorthand
        var useSubscribe = section.Get<SubscribeConfigurationModel>()?.Subscribe == true;

        if (useSubscribe)
        {
            services.AddSingleton<IJobSource, ActiveMqSubscribeJobSource>();
        }
        else
        {
            services.AddSingleton<IJobSource, ActiveMqJobSource>();
        }

        return services
            // Required
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .AddSingleton<IActiveMqSubscribeConfigurationService>(
                new ActiveMqSubscribeConfigurationService(useSubscribe))
            .Configure<ActiveMqConfigurationModel>(section)
            .Configure<ActiveMqServerConfigurationSource.ConfigurationModel>(section)
            .AddSingleton<IActiveMqServerConfigurationSource, ActiveMqServerConfigurationSource>()
            .AddSingleton<IInnerActiveMqConnectionFactory, InnerActiveMqConnectionFactory>()
            .AddSingleton<IActiveMqConnectionFactory, ActiveMqConnectionFactory>()
            .AddSingleton<IActiveMqExceptionArbiterService, ActiveMqExceptionArbiterService>()
            .AddSingleton<IActiveMqSubscribeExceptionArbiter, ActiveMqSubscribeExceptionArbiterService>()
            .AddSingleton<IActiveMqRetryWrapperService, ActiveMqRetryWrapperService>()
            .AddSingleton<IActiveMqConsumerRetryWrapper, ActiveMqConsumerRetryWrapper>();
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