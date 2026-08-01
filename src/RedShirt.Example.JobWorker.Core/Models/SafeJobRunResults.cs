namespace RedShirt.Example.JobWorker.Core.Models;

internal sealed class SafeJobRunResults
{
    public required bool JobSuccess { get; init; }
    public required Exception? Exception { get; init; }
}