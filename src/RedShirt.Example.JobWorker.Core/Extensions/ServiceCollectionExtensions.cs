using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RedShirt.Example.JobWorker.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        return services;
    }
}