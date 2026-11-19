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

    public required bool EnableTraceLogging { get; init; }

    /// <summary>
    ///     Namespace for idempotency cache and lock keys, so that deployments sharing one Redis do not see each other's
    ///     message IDs. Give each deployment that consumes a different source its own value.
    ///     Defaults to the application name (<see cref="AppDomain.FriendlyName" />) when not set.
    ///     Must not contain a colon, which separates key segments.
    /// </summary>
    public string? KeyNamespace { get; init; }

    public string EffectiveKeyNamespace => string.IsNullOrWhiteSpace(KeyNamespace)
        ? AppDomain.CurrentDomain.FriendlyName
        : KeyNamespace.Trim();

    /// <summary>
    ///     Startup check: the effective namespace must not contain a colon, or two namespaces could produce the same key.
    /// </summary>
    internal static bool IsValid(IdempotencyConfigurationModel model)
    {
        return !model.EffectiveKeyNamespace.Contains(':');
    }
}