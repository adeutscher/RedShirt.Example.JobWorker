using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPulsarJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSingleton<IJobSource, PulsarJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            .Configure<PulsarConsumerFactory.ConfigurationModel>(configuration.GetSection("JobSource:Pulsar"))
            .AddSingleton<IPulsarConsumerFactory, PulsarConsumerFactory>()
            .AddSingleton<IPulsarConsumerSource, PulsarConsumerSource>()
            .AddSingleton<IPulsarExceptionArbiterService, PulsarExceptionArbiterService>()
            .AddSingleton<IPulsarRetryWrapperService, PulsarRetryWrapperService>()
            .AddSingleton<IPulsarMessageSource, PulsarMessageSource>();
    }
}
