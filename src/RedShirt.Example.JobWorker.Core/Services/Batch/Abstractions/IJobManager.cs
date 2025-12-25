using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;

/// <summary>
///     The Job Manager is responsible for acting on a series of jobs returned from a source.
/// </summary>
internal interface IJobManager
{
    Task RunAsync(List<IJobModel> items, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
}