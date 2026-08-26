using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services.Configuration;

/// <summary>
///     Provides centralized access to core runtime configuration values to be available in Core and job source projects.
/// </summary>
public interface ICoreConfigurationService
{
    /// <summary>
    ///     Maximum number of jobs the worker should fetch and hold in-flight.
    ///     Callers may assume the returned value is at least <c>1</c>.
    /// </summary>
    int FetchCount { get; }

    /// <summary>
    ///     Indicates that the application should be stopped in the event of a serious unhandled exception.
    ///     A graceful stop should be initiated by feeding the caught exception into <see cref="IExecutionEndArbiter" />.
    /// </summary>
    bool IsHaltOnFailure { get; }

    /// <summary>
    ///     When <c>true</c>, transient exceptions are escalated and treated as unexpected errors.
    ///     Largely intended for debugging some cases without having to temporarily break exception handling in code.
    /// </summary>
    bool IsTreatingTransientExceptionAsFailure { get; }
}

internal sealed class CoreConfigurationService(
    IOptions<CoreConfigurationModel> coreOptions,
    IOptions<JobSourceConfigurationModel> jobSourceOptions) : ICoreConfigurationService
{
    public int FetchCount => jobSourceOptions.Value.EffectiveFetchCount;

    public bool IsHaltOnFailure => coreOptions.Value.HaltOnFailure;

    public bool IsTreatingTransientExceptionAsFailure => coreOptions.Value.TreatTransientExceptionAsFailure;
}