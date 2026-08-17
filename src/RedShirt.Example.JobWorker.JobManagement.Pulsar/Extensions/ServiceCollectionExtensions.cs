using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPulsarJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        var section = configuration.GetSection("JobSource:Pulsar");
        return services
            .AddSingleton<IJobSource, PulsarJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            .Configure<PulsarConsumerFactory.ConfigurationModel>(section)
            .AddSingleton<IPulsarConsumerFactory, PulsarConsumerFactory>()
            .AddSingleton<IPulsarConsumerSource, PulsarConsumerSource>()
            .AddSingleton<IPulsarExceptionArbiterService, PulsarExceptionArbiterService>()
            .AddSingleton<IPulsarRetryWrapperService, PulsarRetryWrapperService>()
            .AddSingleton<IPulsarMessageSource, PulsarMessageSource>()
            .Configure<PulsarMessageSource.ConfigurationModel>(section);
    }
}