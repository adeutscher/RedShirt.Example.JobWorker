using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

public interface IJobLogicRunner
{
    Task RunAsync(IJobDataModel job, CancellationToken cancellationToken = default);
}