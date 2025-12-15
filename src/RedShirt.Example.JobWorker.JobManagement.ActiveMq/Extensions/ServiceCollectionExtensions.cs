using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.Common.Extensions;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActiveMqJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCommonJobManagement(configuration)
            // Required
            .AddSingleton<IJobSource, ActiveMqJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<ActiveMqJobSource.ConfigurationModel>(configuration.GetSection("JobSource:ActiveMq"))
            .AddSingleton<IActiveMqMessageBodyRetriever, ActiveMqMessageBodyRetriever>()
            .AddSingleton<IInnerActiveMqConnectionFactory, InnerActiveMqConnectionFactory>()
            .AddSingleton<IActiveMqConnectionFactory, ActiveMqConnectionFactory>();
    }
}