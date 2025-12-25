using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Batch;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Loader;

namespace RedShirt.Example.JobWorker.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        services = services
            // General
            .AddSingleton<ISourceMessageSorter, SourceMessageSorter>()
            .AddSingleton<ISafeJobRunner, SafeJobRunner>()
            .AddSingleton<IExecutionEndArbiter, ExecutionEndArbiter>()
            .Configure<SafeJobRunner.ConfigurationModel>(configuration.GetSection("Jobs"))
            .Configure<JobSourceConfigurationModel>(configuration.GetSection("JobSource"))
            .Configure<LoopOptionsConfigurationModel>(configuration.GetSection("Jobs"))
            .Configure<ThreadConfigurationModel>(configuration.GetSection("Jobs"));

        var useLoaderModeRaw = configuration.GetValue("Jobs:Loader:Enabled", "0");

        if (int.TryParse(useLoaderModeRaw, out var useLoaderMode) && useLoaderMode == 1)
        {
            // Loader Mode (Experimental)
            services = services
                .AddSingleton<IHandler, LoaderHandler>()
                .AddSingleton<ILoaderExecutionEndArbiter, LoaderExecutionEndArbiter>()
                .AddSingleton<IExecutor, Executor>()
                .AddSingleton<IJobRepository, JobRepository>()
                .Configure<JobRepository.ConfigurationModel>(configuration.GetSection("Jobs:Loader"))
                .AddSingleton<IMaintainer, Maintainer>()
                .AddSingleton<IHeartbeatCalculator, HeartbeatCalculator>()
                .AddSingleton<IJobLoader, JobLoader>();
        }
        else
        {
            // Batch Mode
            services = services
                .AddSingleton<IHandler, BatchHandler>()
                .AddSingleton<IJobManager, JobManager>()
                .AddSingleton<IBatchWorkerLoop, BatchWorkerLoop>();
        }

        return services;
    }
}