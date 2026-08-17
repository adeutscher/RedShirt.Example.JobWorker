namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;

/// <summary>
///     Job configuration
/// </summary>
internal sealed class AzureServiceBusConfigurationModel
{
    public required int MaxMessagesPerRequest { get; set; }
    public required int VisibilityTimeoutSeconds { get; init; }
    public required int WaitTimeSeconds { get; init; }
    public int EffectiveWaitTimeSeconds => Math.Max(0, WaitTimeSeconds);
    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);
}