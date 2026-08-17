using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ConfigPrefix = "JobSource:NATS";

    public static IServiceCollection AddNatsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        var section = configuration.GetSection(ConfigPrefix);

        return services
            // Required
            .AddSingleton<IJobSource, NatsJobSource>()
            .Configure<NatsStreamConfigurationModel>(section)
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting (also required)
            .Configure<NatsCredentialSource.ConfigurationModel>(section)
            .AddSingleton<INatsCredentialSource, NatsCredentialSource>()
            .AddSingleton<INatsMessageSource, NatsMessageSource>()
            .Configure<NatsMessageSource.ConfigurationModel>(section)
            .AddSingleton<INatsJetStreamContextFactory, NatsJetStreamContextFactory>()
            .Configure<NatsJetStreamContextFactory.ConfigurationModel>(section)
            .AddSingleton<INatsConsumerSource, NatsConsumerSource>();
    }
}