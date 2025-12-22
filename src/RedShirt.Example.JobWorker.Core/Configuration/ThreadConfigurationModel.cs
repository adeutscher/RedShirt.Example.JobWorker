namespace RedShirt.Example.JobWorker.Core.Configuration;

internal class ThreadConfigurationModel
{
    public int EffectiveWorkerThreadCount => Math.Max(1, WorkerThreadCount);
    public required int WorkerThreadCount { get; init; }
}