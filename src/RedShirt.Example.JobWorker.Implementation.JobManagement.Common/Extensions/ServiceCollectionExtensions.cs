using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCoreJobManagement(configuration)
            .AddSingleton<ISourceMessageConverter, SourceMessageConverter>();
    }
}