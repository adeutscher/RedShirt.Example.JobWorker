using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSingleton<IExecutionEndArbiter, ExecutionEndArbiter>()
            .AddSingleton<IHandler, Handler>()
            .AddSingleton<IJobManager, JobManager>()
            .Configure<JobManager.ConfigurationModel>(configuration.GetSection("Jobs"))
            .AddSingleton<ISafeJobRunner, SafeJobRunner>()
            .AddSingleton<IWorkerLoop, WorkerLoop>()
            .Configure<WorkerLoop.ConfigurationModel>(configuration.GetSection("Jobs"));
    }
}