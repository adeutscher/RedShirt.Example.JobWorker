namespace RedShirt.Example.JobWorker.Core.Configuration;

internal sealed class JobSourceConfigurationModel
{
    /// <summary>
    ///     Maximum number of jobs the worker should fetch and hold in-flight.
    ///     Callers may assume the returned value is at least <c>1</c>.
    /// </summary>
    public required int FetchCount { get; init; }

    public int EffectiveFetchCount => Math.Max(FetchCount, 1);
}