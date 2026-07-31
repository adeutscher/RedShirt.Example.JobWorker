using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Azure.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonAzureServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<IAzureExceptionArbiterService, AzureExceptionArbiterService>()
            .AddSingleton<IAzureRetryWrapperService, AzureRetryWrapperService>();
    }
}