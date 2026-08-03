namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Enums;

/// <summary>
///     Outcome of consumer-side poison-message enforcement for Google Pub/Sub.
/// </summary>
internal enum PoisonEnforcementResult
{
    /// <summary>
    ///     Configuration indicates a dead-letter topic is configured at the Pub/Sub level,
    ///     so this consumer does not acknowledge poison messages away.
    /// </summary>
    EnforcementNotEnabled,

    /// <summary>
    ///     Consumer-side enforcement is enabled, but the message still has available delivery attempts remaining.
    /// </summary>
    NotEnforced,

    /// <summary>
    ///     The message was acknowledged by the consumer-based enforcement policy
    ///     (delivery attempt at or above the maximum).
    /// </summary>
    Enforced
}
