using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.Services.Configuration;

/// <summary>
///     Provides access to core runtime configuration values.
/// </summary>
public interface ICoreConfigurationService
{
    int GetBacklogSize();
    bool IsHaltOnFailure();
}

internal sealed class CoreConfigurationService(
    IOptions<CoreConfigurationModel> coreOptions,
    IOptions<JobRepository.ConfigurationModel> jobRepositoryOptions) : ICoreConfigurationService
{
    public bool IsHaltOnFailure()
    {
        return coreOptions.Value.HaltOnFailure;
    }

    public int GetBacklogSize()
    {
        return jobRepositoryOptions.Value.EffectiveBacklogSize;
    }
}