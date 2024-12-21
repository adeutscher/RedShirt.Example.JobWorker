using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

public interface IJobFailureHandler
{
    Task HandleFailureAsync(IJobModel jobModel, Exception exception, CancellationToken cancellationToken = default);
}