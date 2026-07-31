namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;

/// <summary>
///     Job configuration for Google Pub/Sub pull subscriptions.
/// </summary>
internal sealed class GooglePubSubConfigurationModel
{
    /// <summary>
    ///     Pub/Sub ack deadlines are capped at 600 seconds by the service.
    /// </summary>
    public const int MaximumAckDeadlineSeconds = 600;

    /// <summary>
    ///     Pub/Sub rejects custom ack deadlines below 10 seconds.
    /// </summary>
    public const int MinimumAckDeadlineSeconds = 10;

    public required string ProjectId { get; init; }
    public required string SubscriptionId { get; init; }
    public required int MaxMessagesPerRequest { get; set; }
    public required int VisibilityTimeoutSeconds { get; init; }

    /// <summary>
    ///     Configuration indication that the Pub/Sub subscription is not configured with a dead-letter topic.
    ///     I strongly encourage anyone reading this to configure a dead-letter topic for every subscription.
    ///     In the event that this is set to true, then this job source implementation will attempt to take more actions to
    ///     deal with poison messages (acknowledge after <see cref="MaximumReceives" /> delivery attempts).
    ///     Calling this DlqNotEnabled does create a bit of a double-negative situation, but this is considered acceptable as
    ///     my overall goal is to nudge the developer to configure a dead-letter topic at the Pub/Sub level.
    /// </summary>
    public required bool DlqNotEnabled { get; init; }

    /// <summary>
    ///     This is used in the event that the DlqNotEnabled property is set to true.
    ///     Attempt to give received messages a chance to be reprocessed before acknowledging them away.
    ///     Once again, I strongly encourage anyone reading this to configure a dead-letter topic for every subscription.
    ///     Note: <c>ReceivedMessage.DeliveryAttempt</c> is only populated by Pub/Sub when a dead-letter policy exists;
    ///     without it the count is typically 0 and app-level enforcement cannot see prior receives.
    /// </summary>
    public required int MaximumReceives { get; init; }

    public int EffectiveMaximumReceives => Math.Max(1, MaximumReceives);

    public int EffectiveVisibilityTimeoutSeconds =>
        Math.Min(Math.Max(MinimumAckDeadlineSeconds, VisibilityTimeoutSeconds), MaximumAckDeadlineSeconds);
}