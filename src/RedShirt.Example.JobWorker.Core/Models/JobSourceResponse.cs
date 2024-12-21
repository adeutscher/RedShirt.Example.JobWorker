namespace RedShirt.Example.JobWorker.Core.Models;

public class JobSourceResponse
{
    public required int RecommendedHeartbeatIntervalSeconds { get; init; }
    public required List<IJobModel> Items { get; init; }
}