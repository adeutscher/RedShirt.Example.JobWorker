using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Logic.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreLogic(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCoreJobManagement(configuration)
            .AddSingleton<IJobLogicRunner, JobLogicRunner>();
    }
}