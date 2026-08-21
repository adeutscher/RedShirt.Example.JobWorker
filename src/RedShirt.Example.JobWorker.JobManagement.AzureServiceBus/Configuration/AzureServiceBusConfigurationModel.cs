namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;

/// <summary>
///     Job configuration
/// </summary>
internal sealed class AzureServiceBusConfigurationModel
{
    public required int MaxMessagesPerRequest { get; set; }
    public required int VisibilityTimeoutSeconds { get; init; }
    public required int WaitTimeSeconds { get; init; }

    /// <summary>
    ///     Tells the job source whether to explicitly Abandon failed messages (as Azure Service Bus allows us)
    ///     or let them float a moment before going back to the queue (as Azure Queue Storage or SQS does)
    /// </summary>
    public required bool AbandonRecoveredFailuresOnAcknowledge { get; init; }

    public int EffectiveWaitTimeSeconds => Math.Max(0, WaitTimeSeconds);
    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);
}