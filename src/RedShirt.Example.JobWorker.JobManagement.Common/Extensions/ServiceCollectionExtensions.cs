using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonJobManagement(this IServiceCollection services)
    {
        return services
            .AddSingleton<ISourceMessageConverter, SourceMessageConverter>();
    }
}