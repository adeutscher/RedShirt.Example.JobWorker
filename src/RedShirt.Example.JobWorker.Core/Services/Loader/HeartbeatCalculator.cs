using RedShirt.Example.JobWorker.Core.Models.Loader;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.Loader;

/// <summary>
///     The heartbeat check exists to abstract date calculations, making reading/testing the Maintainer simpler.
/// </summary>
internal interface IHeartbeatCalculator
{
    bool IsReadyForHeartbeat(IJobRepositoryEntry entry);
    TimeSpan TimeUntilNextHeartbeat(IJobRepositoryEntry entry);
}

internal class HeartbeatCalculator(IJobSource jobSource) : IHeartbeatCalculator
{
    public bool IsReadyForHeartbeat(IJobRepositoryEntry entry)
    {
        return TimeUntilNextHeartbeat(entry) == TimeSpan.Zero;
    }

    public TimeSpan TimeUntilNextHeartbeat(IJobRepositoryEntry entry)
    {
        var recommendedHeartbeat =
            entry.LastHeartbeatTime + TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds);

        if (recommendedHeartbeat > DateTime.UtcNow)
        {
            return recommendedHeartbeat - DateTime.UtcNow;
        }

        return TimeSpan.Zero; // Recommended heartbeat time: Now
    }
}