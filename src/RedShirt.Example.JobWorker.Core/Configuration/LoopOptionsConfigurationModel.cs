namespace RedShirt.Example.JobWorker.Core.Configuration;

internal sealed class LoopOptionsConfigurationModel
{
    public int EffectiveMaxIdleWaitSeconds => Math.Max(1, MaxIdleWaitSeconds);
    public required int MaxIdleWaitSeconds { get; init; }
}