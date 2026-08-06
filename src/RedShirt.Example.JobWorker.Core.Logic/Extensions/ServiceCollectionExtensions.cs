using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Logic.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreLogic(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSingleton<IJobLogicRunner, JobLogicRunner>();
    }
}