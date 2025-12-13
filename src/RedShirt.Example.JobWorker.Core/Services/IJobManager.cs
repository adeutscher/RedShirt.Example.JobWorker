using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface IJobManager
{
    Task RunAsync(JobSourceResponse response, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
}