using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     BatchHandler of actual job logic. Used by Core.Logic project.
/// </summary>
public interface IJobLogicRunner
{
    Task<JobResult> RunAsync(IJobModel job, CancellationToken cancellationToken = default);
}