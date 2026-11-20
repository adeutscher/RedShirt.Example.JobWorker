namespace RedShirt.Example.JobWorker.Common.Health.Configuration;

public sealed class CommonHealthConfigurationModel
{
    public const string SectionName = "Health";

    /// <summary>
    ///     Health requests are cheap and normally come from one or two probes, so a small limit is enough.
    /// </summary>
    public const int DefaultMaximumConcurrentRequests = 4;

    public bool Enabled { get; init; } = true;

    public int Port { get; init; } = 8080;

    /// <summary>
    ///     How many health requests are handled at once. Further requests wait until one finishes.
    ///     Unset or not positive means <see cref="DefaultMaximumConcurrentRequests" />.
    /// </summary>
    public int? MaximumConcurrentRequests { get; init; }

    public int EffectiveMaximumConcurrentRequests =>
        MaximumConcurrentRequests is > 0 ? MaximumConcurrentRequests.Value : DefaultMaximumConcurrentRequests;
}
