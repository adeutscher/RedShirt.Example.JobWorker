using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNatsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
                // Required
                .AddSingleton<IJobSource, NatsJobSource>()
                .Configure<NatsJobSource.ConfigurationModel>(configuration.GetSection("JobSource:NATS"))
                .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
                // Supporting
                .Configure<NatsCredentialSource.ConfigurationModel>(configuration.GetSection("JobSource:NATS"))
                .AddSingleton<INatsCredentialSource, NatsCredentialSource>()
                .AddSingleton<INatsMessageSource, NatsMessageSource>()
                .AddSingleton<INatsJetStreamContextFactory, NatsJetStreamContextFactory>()
                .Configure<NatsJetStreamContextFactory.ConfigurationModel>(configuration.GetSection("JobSource:NATS"))
            ;
    }
}