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
    ///     An exception was thrown during job execution. May be recoverable on retry.
    /// </summary>
    Failure,

    /// <summary>
    ///     Processing was cancelled (for example, via <see cref="System.Threading.CancellationToken" />).
    ///     Recoverable on a later delivery/retry.
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
    ///     A miscellaneous, explicitly unrecoverable problem (for example, body retrieval failed).
    /// </summary>
    Broken
}
