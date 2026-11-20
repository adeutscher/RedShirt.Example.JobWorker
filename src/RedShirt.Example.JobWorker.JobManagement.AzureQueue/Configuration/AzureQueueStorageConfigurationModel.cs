namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;

/// <summary>
///     Job configuration
/// </summary>
internal sealed class AzureQueueStorageConfigurationModel
{
    /// <summary>
    ///     Receives allowed for a message when <see cref="MaximumReceives" /> is not set.
    ///     Matches the default <c>maxDequeueCount</c> of Azure Functions queue triggers.
    /// </summary>
    public const int DefaultMaximumReceives = 5;

    public required int VisibilityTimeoutSeconds { get; init; }
    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);

    /// <summary>
    ///     Maximum receives of a message that keeps failing. Azure Queue Storage has no dead-letter queue, so a message
    ///     that fails with a recoverable failure on its last receive is deleted. A message that was already received more
    ///     times than this without being acknowledged (for example, because it crashes the worker) is deleted when it is
    ///     received, without running it.
    ///     Unset or not positive means <see cref="DefaultMaximumReceives" />.
    /// </summary>
    public int? MaximumReceives { get; init; }

    public int EffectiveMaximumReceives =>
        MaximumReceives is > 0 ? MaximumReceives.Value : DefaultMaximumReceives;
}