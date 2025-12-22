using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Batch;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // General
            .AddSingleton<IExecutionEndArbiter, ExecutionEndArbiter>()
            .AddSingleton<ISafeJobRunner, SafeJobRunner>()
            .Configure<SafeJobRunner.ConfigurationModel>(configuration.GetSection("Jobs"))
            .Configure<JobSourceConfigurationModel>(configuration.GetSection("JobSource"))
            .Configure<ThreadConfigurationModel>(configuration.GetSection("Jobs"))
            // Batch Mode
            .AddSingleton<IHandler, BatchHandler>()
            .AddSingleton<IJobManager, JobManager>()
            .AddSingleton<IBatchWorkerLoop, BatchWorkerLoop>()
            .Configure<BatchWorkerLoop.ConfigurationModel>(configuration.GetSection("Jobs"));
    }
}