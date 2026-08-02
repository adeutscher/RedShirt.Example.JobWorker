namespace RedShirt.Example.JobWorker.Core.Enums;

/// <summary>
///     Outcome returned by application job logic (<see cref="Services.Abstractions.IJobLogicRunner" />).
/// </summary>
public enum JobResult
{
    /// <summary>
    ///     Job logic completed successfully.
    /// </summary>
    Success,

    /// <summary>
    ///     Job logic identified an unrecoverable problem.
    ///     This unrecoverable problem is translated back to the job source,
    ///     which may choose to handle it differently than a regular failure.
    /// </summary>
    Broken
}