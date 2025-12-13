using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Handler of actual job logic. Used by Core.Logic project.
/// </summary>
public interface IJobLogicRunner
{
    Task RunAsync(IJobDataModel job, CancellationToken cancellationToken = default);
}