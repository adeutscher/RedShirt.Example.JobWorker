using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     Defines a generic job source.
/// </summary>
public interface IJobSource
{
    /// <summary>
    ///     Dictates how long is recommended to wait until sending a heartbeat for a message.
    ///     Can be assumed to be unchanging during execution.
    ///     The recommended value for this property is 75% of the message in-flight time.
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds { get; }

    Task AcknowledgeCompletionAsync(IJobModel message, bool success, CancellationToken cancellationToken = default);
    Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default);
    Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default);
}