namespace RedShirt.Example.JobWorker.Core.Configuration;

internal sealed class JobSourceConfigurationModel
{
    public required int BatchSize { get; init; }
    public int EffectiveBatchSize => Math.Max(BatchSize, 1);
}