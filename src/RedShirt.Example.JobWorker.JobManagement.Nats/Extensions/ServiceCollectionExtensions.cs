using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ConfigPrefix = "JobSource:NATS";

    public static IServiceCollection AddNatsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        var section = configuration.GetSection(ConfigPrefix);
        var useSubscribe = section.Get<SubscribeConfigurationModel>()?.Subscribe == true;

        if (useSubscribe)
        {
            services.AddSingleton<IJobSource, NatsSubscribeJobSource>();
        }
        else
        {
            services.AddSingleton<IJobSource, NatsJobSource>();
        }

        return services
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            .Configure<NatsStreamConfigurationModel>(section)
            .Configure<NatsCredentialSource.ConfigurationModel>(section)
            .AddSingleton<INatsCredentialSource, NatsCredentialSource>()
            .Configure<NatsMessageSource.ConfigurationModel>(section)
            .AddSingleton<INatsMessageSource, NatsMessageSource>()
            .Configure<NatsJetStreamContextFactory.ConfigurationModel>(section)
            .AddSingleton<INatsJetStreamContextFactory, NatsJetStreamContextFactory>()
            .AddSingleton<INatsConnectionCacheSource, NatsConnectionCacheSource>()
            .AddSingleton<INatsConsumerSource, NatsConsumerSource>()
            .AddSingleton<INatsExceptionArbiterService, NatsExceptionArbiterService>()
            .AddSingleton<INatsRetryWrapperService, NatsRetryWrapperService>()
            .AddSingleton<INatsConnectionRetryWrapper, NatsConnectionRetryWrapper>();
    }

    private sealed class SubscribeConfigurationModel
    {
#pragma warning disable S3459
#pragma warning disable S1144
        public required bool Subscribe { get; init; }
#pragma warning restore S1144
#pragma warning restore S3459
    }
}