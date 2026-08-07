using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Services;

namespace RedShirt.Example.JobWorker.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommon(this IServiceCollection services)
    {
        return services
            .AddSingleton<ISleepService, SleepService>();
    }
}