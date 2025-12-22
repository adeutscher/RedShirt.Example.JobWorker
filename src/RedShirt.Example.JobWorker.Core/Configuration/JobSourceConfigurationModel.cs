namespace RedShirt.Example.JobWorker.Core.Configuration;

internal class JobSourceConfigurationModel
{
    public required int BatchSize { get; init; }
    public int EffectiveBatchSize => Math.Max(BatchSize, 1);
}