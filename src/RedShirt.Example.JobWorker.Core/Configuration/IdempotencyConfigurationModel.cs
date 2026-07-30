namespace RedShirt.Example.JobWorker.Core.Configuration;

internal sealed class IdempotencyConfigurationModel
{
    public required bool Enabled { get; init; }

    public required int ResultCacheDurationSeconds { get; init; }
    public int EffectiveResultCacheDurationSeconds => Math.Max(10, ResultCacheDurationSeconds);

    public required int MonitorIntervalSeconds { get; init; }
    public int EffectiveMonitorIntervalSeconds => Math.Max(3, MonitorIntervalSeconds);

    /// <summary>
    ///     If true, then treat idempotency IDs as though there is a reasonable chance that they could repeat.
    ///     If false, then idempotency services will attempt to optimize cache performance by identifying cases
    ///     where caching the result of a job is not necessary.
    /// </summary>
    public required bool IdempotencyIdsCanRepeat { get; init; }
}