namespace RedShirt.Example.JobWorker.Core.Enums;

/// <summary>
///     Outcome of a job from the Core worker's perspective (intake, execution, and acknowledgement).
/// </summary>
public enum CoreJobResult
{
    /// <summary>
    ///     Job completed successfully.
    /// </summary>
    Success,

    /// <summary>
    ///     An exception was thrown during job execution or the job application logic
    ///     noted it as a failure without an exception.
    ///     May be recoverable on retry.
    /// </summary>
    Failure,

    /// <summary>
    ///     Processing was cancelled (for example, via <see cref="System.Threading.CancellationToken" />) by an uncaught
    ///     <see cref="OperationCanceledException" />. Recoverable on a later delivery/retry.
    /// </summary>
    Cancelled,

    /// <summary>
    ///     The message body was empty.
    /// </summary>
    Empty,

    /// <summary>
    ///     An error occurred while converting the message body into a job model.
    /// </summary>
    Parsing,

    /// <summary>
    ///     Indicates that the job application logic handler identified an explicitly unrecoverable problem
    ///     (for example, invalid data based on business logic).
    /// </summary>
    Broken
}