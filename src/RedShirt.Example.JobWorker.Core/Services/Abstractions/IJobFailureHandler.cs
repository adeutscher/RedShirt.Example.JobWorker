using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     To be sent when a job throws an unexpected exception (or too many JobRetryExceptions).
/// </summary>
public interface IJobFailureHandler
{
    Task HandleFailureAsync(IJobModel jobModel, Exception exception, CancellationToken cancellationToken = default);
}