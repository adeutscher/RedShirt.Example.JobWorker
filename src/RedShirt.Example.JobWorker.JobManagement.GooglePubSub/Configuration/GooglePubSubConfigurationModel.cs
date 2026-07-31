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

    public int EffectiveVisibilityTimeoutSeconds =>
        Math.Min(Math.Max(MinimumAckDeadlineSeconds, VisibilityTimeoutSeconds), MaximumAckDeadlineSeconds);
}
