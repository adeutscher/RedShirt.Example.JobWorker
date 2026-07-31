using RedShirt.Example.JobWorker.Core.Exceptions;
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
    /// <returns></returns>
    /// <exception cref="WorkerJobSourceException">
    ///     Thrown when acknowledgement fails against the underlying message source.
    ///     When <see cref="WorkerJobSourceException.IsTransient" /> is <c>true</c>, callers may retry;
    ///     when <c>false</c>, the failure should be treated as permanent.
    ///     When <see cref="WorkerJobSourceException.IsHandled" /> is <c>true</c>, a job-source retry wrapper
    ///     has already exhausted retries and callers should not retry again.
    ///     When <see cref="WorkerJobSourceException.IsCritical" /> is <c>true</c>, callers should surface the failure.
    /// </exception>
    Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default);

    Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Extend the in-flight / visibility window for a job record, if the underlying message source supports it.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="WorkerJobSourceException">
    ///     Thrown when the heartbeat / visibility extension fails against the underlying message source.
    ///     When <see cref="WorkerJobSourceException.IsTransient" /> is <c>true</c>, callers may retry;
    ///     when <c>false</c>, the failure should be treated as permanent (for example, the message can no longer
    ///     have its flight time extended).
    ///     When <see cref="WorkerJobSourceException.IsHandled" /> is <c>true</c>, a job-source retry wrapper
    ///     has already exhausted retries and callers should not retry again.
    ///     When <see cref="WorkerJobSourceException.IsCritical" /> is <c>true</c>, callers should surface the failure.
    /// </exception>
    Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default);
}