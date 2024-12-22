using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

public interface IJobSource
{
    Task AcknowledgeCompletionAsync(IJobModel message, bool success, CancellationToken cancellationToken = default);
    Task<JobSourceResponse> GetJobsAsync(CancellationToken cancellationToken = default);
    Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default);
}