namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;

/// <summary>
///     Job configuration
/// </summary>
internal sealed class AzureQueueStorageConfigurationModel
{
    public required int VisibilityTimeoutSeconds { get; init; }
    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);
}