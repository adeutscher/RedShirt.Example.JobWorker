namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Enums;

/// <summary>
///     Outcome of consumer-side poison-message enforcement for SQS.
/// </summary>
internal enum PoisonEnforcement
{
    /// <summary>
    ///     Configuration indicates a DLQ is configured at the AWS level, so this consumer does not delete poison messages.
    /// </summary>
    EnforcementNotEnabled,

    /// <summary>
    ///     Consumer-side enforcement is enabled, but the message still has available receive attempts remaining.
    /// </summary>
    NotEnforced,

    /// <summary>
    ///     The message was deleted by the consumer-based enforcement policy (receive count at or above the maximum).
    /// </summary>
    Enforced
}