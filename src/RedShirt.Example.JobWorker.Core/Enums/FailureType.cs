namespace RedShirt.Example.JobWorker.Core.Enums;

/// <summary>
///     Classification of a failed job for <see cref="Services.Abstractions.IJobFailureHandler" />.
/// </summary>
public enum FailureType
{
    /// <summary>
    ///     The message body was empty.
    /// </summary>
    Empty,

    /// <summary>
    ///     An error occurred while converting the message body into a job model.
    /// </summary>
    Parsing,

    /// <summary>
    ///     An exception was thrown during job execution.
    /// </summary>
    Execution,

    /// <summary>
    ///     A miscellaneous, explicitly unrecoverable problem.
    /// </summary>
    Broken
}
