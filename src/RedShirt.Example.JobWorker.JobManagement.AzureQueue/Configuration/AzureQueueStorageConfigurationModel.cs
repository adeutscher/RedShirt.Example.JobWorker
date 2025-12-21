namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;

/// <summary>
///     Job configuration
/// </summary>
internal sealed class AzureQueueStorageConfigurationModel
{
    public required int BatchSize { get; init; }
    public required int VisibilityTimeoutSeconds { get; init; }
    public int EffectiveBatchSize => Math.Max(BatchSize, 1);
    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);
}