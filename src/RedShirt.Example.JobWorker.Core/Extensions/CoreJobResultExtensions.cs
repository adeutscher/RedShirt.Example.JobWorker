using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Extensions;

/// <summary>
///     Helpers for interpreting <see cref="CoreJobResult" />.
/// </summary>
public static class CoreJobResultExtensions
{
    /// <summary>
    ///     Returns <c>true</c> when the failure may succeed on a later delivery/retry
    ///     (<see cref="CoreJobResult.Failure" /> / <see cref="FailureType.Execution" />, or
    ///     <see cref="CoreJobResult.Cancelled" /> / <see cref="FailureType.Cancelled" />).
    ///     <see cref="CoreJobResult.Empty" />, <see cref="CoreJobResult.Parsing" />, and
    ///     <see cref="CoreJobResult.InvalidData" /> are not recoverable.
    /// </summary>
    public static bool IsRecoverableFailure(this CoreJobResult result)
    {
        return result is CoreJobResult.Failure or CoreJobResult.Cancelled;
    }

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="result" /> is <see cref="CoreJobResult.Success" />.
    ///     Exists for consistency with <see cref="IsRecoverableFailure" /> rather than to obscure a single-value check.
    /// </summary>
    public static bool IsSuccessful(this CoreJobResult result)
    {
        return result == CoreJobResult.Success;
    }

    /// <summary>
    ///     Maps a non-success <see cref="CoreJobResult" /> to a <see cref="FailureType" />.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="result" /> is <see cref="CoreJobResult.Success" />.
    /// </exception>
    public static FailureType ToFailureType(this CoreJobResult result)
    {
        return result switch
        {
            CoreJobResult.Empty => FailureType.Empty,
            CoreJobResult.Parsing => FailureType.Parsing,
            CoreJobResult.Failure => FailureType.Execution,
            CoreJobResult.Cancelled => FailureType.Cancelled,
            CoreJobResult.InvalidData => FailureType.Broken,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result,
                "Success has no corresponding failure type.")
        };
    }
}