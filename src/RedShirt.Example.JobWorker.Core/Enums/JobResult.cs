using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Enums;

/// <summary>
///     Outcome returned by a job's application logic (<see cref="Services.Abstractions.IJobLogicRunner" />).
/// </summary>
public enum JobResult
{
    /// <summary>
    ///     Job application logic completed successfully.
    /// </summary>
    Success,

    /// <summary>
    ///     Job logic reported a recoverable failure without throwing.
    ///     Handled the same way as an exception in the job application logic not being caught;
    ///     maps to <see cref="CoreJobResult.Failure" />.
    ///     A failure does not rule out the possibility that the job could be retried successfully,
    ///     which the job source may choose to take into consideration when it is tasked with acknowledgement.
    ///     Although it is an option, it is recommended that the developer implementing this template
    ///     consider just letting an exception be thrown. This is especially true if the exception is from a third-party
    ///     library.
    ///     Throwing an exception could give the <see cref="IJobFailureHandler" /> implementation a bit more context
    ///     to pass along when it handles a problem.
    ///     In a way, main whole point of this specific enum value is to provide a staging ground to nicely ask the developer
    ///     to let an exception bubble up instead of using it.
    /// </summary>
    Failure,

    /// <summary>
    ///     Job logic identified an unrecoverable problem.
    ///     This unrecoverable problem is translated back to the job source,
    ///     which may choose to handle it differently than a regular failure.
    ///     To confirm phrasing, a broken job suggests that something is fundamentally
    ///     broken with the provided job data based on implementation-specific business logic.
    ///     A bad database connection would not warrant a Broken response, as that would be an infrastructure
    ///     problem that could work once the underlying cause had been worked out.
    ///     An IJobDataModel payload that provided the words "roseate spoonbill" instead of a valid SHA256 checksum would
    ///     qualify as Broken, as no number of retries could make that input work as intended.
    /// </summary>
    Broken
}