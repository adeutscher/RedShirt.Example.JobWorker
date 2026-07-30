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

    /// <summary>
    ///     Acknowledge attempted processing of a job record.
    ///     This method is to be called regardless of whether the job record was successfully processed or not.
    ///     What is done on success or failure is dependent on the mechanics of the underlying message source.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="success"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    ///     <c>true</c> if acknowledgement succeeded; <c>false</c> if acknowledgement could not be completed
    ///     or was a no-op failure path.
    /// </returns>
    Task<bool> AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default);

    Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Extend the in-flight / visibility window for a job record, if the underlying message source supports it.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    ///     <c>true</c> if the heartbeat succeeded (or was intentionally a no-op for sources that do not use heartbeats);
    ///     <c>false</c> if the heartbeat could not be completed or was a no-op failure path.
    /// </returns>
    Task<bool> HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default);
}