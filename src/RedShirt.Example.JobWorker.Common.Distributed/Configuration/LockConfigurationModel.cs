namespace RedShirt.Example.JobWorker.Common.Distributed.Configuration;

internal sealed class LockConfigurationModel
{
    private const int DefaultTimeoutInSeconds = 10;

    public required int? TimeoutSeconds { get; init; }

    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Max(1, TimeoutSeconds ?? DefaultTimeoutInSeconds));
}