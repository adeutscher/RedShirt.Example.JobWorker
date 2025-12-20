namespace RedShirt.Example.JobWorker.Core.Models;

public sealed class JobSourceResponse
{
    /// <summary>
    ///     Dictates how long is recommended to wait until heartbeating a message.
    /// </summary>
    public required int RecommendedHeartbeatIntervalSeconds { get; init; }

    public required List<IJobModel> Items { get; init; }
}