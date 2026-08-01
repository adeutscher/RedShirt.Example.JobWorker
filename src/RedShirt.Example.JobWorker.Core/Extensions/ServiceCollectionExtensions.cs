using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Heartbeats;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ConfigSectionName = "Jobs";

    public static IServiceCollection AddCoreJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        services = services
            // General
            .AddSingleton<IHandler, Handler>()
            .AddSingleton<IJobLoaderLoop, JobLoaderLoop>()
            .AddSingleton<IJobExecutor, JobExecutor>()
            .AddSingleton<IAppliedExecutionEndArbiter, AppliedExecutionEndArbiter>()
            .AddSingleton<IHeartbeatMaintainer, HeartbeatMaintainer>()
            .AddSingleton<IHeartbeatCalculator, HeartbeatCalculator>()
            .AddSingleton<ISafeJobRunner, SafeJobRunner>()
            .AddSingleton<ISafeJobAcknowledgementService, SafeJobAcknowledgementService>()
            .AddSingleton<IJobIntakeService, JobIntakeService>()
            .AddSingleton<ISleepService, SleepService>()
            .AddSingleton<IExecutionEndArbiter, ExecutionEndArbiter>()
            .AddSingleton<IJobRepository, JobRepository>()
            .Configure<JobRepository.ConfigurationModel>(configuration.GetSection(ConfigSectionName))
            .AddSingleton<IJobLoaderStateService, JobLoaderStateService>()
            .AddSingleton<IJobLoaderStateReaderService>(provider=>provider.GetRequiredService<IJobLoaderStateService>())
            .Configure<SafeJobRunner.ConfigurationModel>(configuration.GetSection(ConfigSectionName))
            .Configure<JobSourceConfigurationModel>(configuration.GetSection("JobSource"))
            .Configure<LoopOptionsConfigurationModel>(configuration.GetSection(ConfigSectionName))
            .Configure<ThreadConfigurationModel>(configuration.GetSection(ConfigSectionName))
            // Idempotency
            .AddSingleton<IIdempotencyMonitor, IdempotencyMonitor>()
            .AddSingleton<IIdempotencyExecutionService, IdempotencyExecutionService>()
            .Configure<IdempotencyConfigurationModel>(configuration.GetSection($"{ConfigSectionName}:Idempotency"))
            // Source Messages
            .AddSingleton<ISourceMessageConverter, SourceMessageConverter>()
            .AddSingleton<ISourceMessageSorter, SourceMessageSorter>();

        if (configuration
                .GetSection(ConfigSectionName).Get<CoreServiceConfigurationModel>()?.EffectiveUseLoaderModeSetting ==
            true)
            // Loader Mode
        {
            services = services
                .AddSingleton<IJobLoader, LoaderModeJobLoader>();
        }
        else
            // Batch Mode
        {
            services = services
                .AddSingleton<IJobLoader, BatchModeJobLoader>();
        }

        return services;
    }

    internal sealed class CoreServiceConfigurationModel
    {
        public required string? UseLoaderMode { get; init; }

        /// <summary>
        ///     Parsing of UseLoaderMode. Felt the need to be a bit more flexible with this parameter, so went with an Effective_
        ///     property.
        /// </summary>
        public bool EffectiveUseLoaderModeSetting => !string.IsNullOrWhiteSpace(UseLoaderMode) &&
                                                     (
                                                         (int.TryParse(UseLoaderMode, out var intResult) &&
                                                          intResult > 0) ||
                                                         (bool.TryParse(UseLoaderMode, out var boolResult) &&
                                                          boolResult));
    }
}