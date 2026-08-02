using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Extensions;

/// <summary>
///     Helpers for interpreting <see cref="FailureType" />.
/// </summary>
public static class FailureTypeExtensions
{
    /// <summary>
    ///     Returns <c>true</c> when the failure may succeed on a later delivery/retry
    ///     (<see cref="FailureType.Execution" /> or <see cref="FailureType.Cancelled" />).
    ///     <see cref="FailureType.Empty" />, <see cref="FailureType.Parsing" />, and
    ///     <see cref="FailureType.Broken" /> are not recoverable.
    /// </summary>
    public static bool IsRecoverable(this FailureType failureType)
    {
        return failureType is FailureType.Execution or FailureType.Cancelled;
    }
}
