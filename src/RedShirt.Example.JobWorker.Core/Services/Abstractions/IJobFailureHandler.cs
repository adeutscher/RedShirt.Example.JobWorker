using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     To be sent when a job throws an unexpected exception (or too many JobRetryExceptions),
///     or when intake classifies a message as a failure (empty, parsing, or broken).
///     The intent behind this interface was to try to maintain a proper separation of concerns
///     between the technology of an IJobSource inheritor and the potentially-different technology of the failure handler.
///     The implementation of this interface is meant to go in the same project as the IJobSource implementation,
///     and can therefore make some more informed decisions based on how that IJobSource implementation's underlying queue
///     technology works.
///     It's admittedly a bit situational. It was originally made for a stream-based job source that
///     could only run messages once, whether they were successful or whether they failed.
///     The fix for this issue was to use this failure handler as an application-defined DLQ with a different technology.
/// </summary>
public interface IJobFailureHandler
{
    Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default);
}