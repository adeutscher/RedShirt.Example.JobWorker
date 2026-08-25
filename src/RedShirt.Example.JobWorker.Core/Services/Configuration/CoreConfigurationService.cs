using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.Services.Configuration;

/// <summary>
///     Provides access to core runtime configuration values.
/// </summary>
public interface ICoreConfigurationService
{
    /// <summary>
    ///     Maximum number of jobs the worker should fetch and hold in-flight.
    ///     Callers may assume the returned value is at least <c>1</c>.
    /// </summary>
    int GetFetchCount();

    bool IsHaltOnFailure();

    /// <summary>
    ///     When true, transient exceptions are escalated and treated as unexpected errors.
    ///     Largely intended for debugging some cases without having to temporarily break exception handling in code.
    /// </summary>
    bool IsTreatingTransientExceptionAsFailure();
}

internal sealed class CoreConfigurationService(
    IOptions<CoreConfigurationModel> coreOptions,
    IOptions<JobRepository.ConfigurationModel> jobRepositoryOptions) : ICoreConfigurationService
{
    public bool IsHaltOnFailure()
    {
        return coreOptions.Value.HaltOnFailure;
    }

    public bool IsTreatingTransientExceptionAsFailure()
    {
        return coreOptions.Value.TreatTransientExceptionAsFailure;
    }

    public int GetFetchCount()
    {
        return jobRepositoryOptions.Value.EffectiveBacklogSize;
    }
}