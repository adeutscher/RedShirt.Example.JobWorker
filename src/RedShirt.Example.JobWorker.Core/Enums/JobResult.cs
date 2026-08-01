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
    /// </summary>
    Broken
}
