using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Logic.Extensions;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Extensions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        return services
            .AddCoreLogic(configuration)
            .AddSqsJobManagement(configuration);
    }
}