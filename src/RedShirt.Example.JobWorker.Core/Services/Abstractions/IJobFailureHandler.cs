using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     To be sent when a job throws an unexpected exception (or too many JobRetryExceptions).
///     The intent behind this interface was to try to maintain a proper separation of concerns
///     between the technology of an IJobSource inheritor and the potentially-different technology of the failure handler.
///     It's admittedly a bit situational. It was originally made for a stream-based job source that
///     could only run messages once, whether they were successful or whether they failed.
///     The solution to this issue was to use this failure handler as an application-defined DLQ.
/// </summary>
public interface IJobFailureHandler
{
    Task HandleFailureAsync(IRawJobModel rawJobModel, Exception? exception,
        CancellationToken cancellationToken = default);
}