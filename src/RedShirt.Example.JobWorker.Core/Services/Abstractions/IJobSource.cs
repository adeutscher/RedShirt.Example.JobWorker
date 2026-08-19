using RedShirt.Example.JobWorker.Core.Enums;
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
    ///     Indicates whether this job source delivers work by subscription rather than polling.
    ///     When <c>false</c>, <see cref="StartSubscriberAsync" /> and <see cref="StopSubscriber" /> shall throw
    ///     <see cref="NotSupportedException" />.
    /// </summary>
    public bool IsSubscriptionSource { get; }

    /// <summary>
    ///     Acknowledge attempted processing of a job record.
    ///     This method is to be called regardless of whether the job record was successfully processed or not.
    ///     What is done on success, recoverable failure, or unrecoverable failure depends on the underlying message source.
    ///     Recoverable failures should typically be left to expire or NAcked for redelivery; unrecoverable results
    ///     should be dead-lettered when the broker supports it.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="result"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="WorkerJobSourceException">
    ///     Thrown when acknowledgement fails against the underlying message source.
    ///     When <see cref="WorkerJobSourceException.CouldBeTransient" /> is <c>true</c>, callers may retry;
    ///     when <c>false</c>, the failure should be treated as permanent.
    ///     When <see cref="WorkerJobSourceException.IsHandled" /> is <c>true</c>, a job-source retry wrapper
    ///     has already exhausted retries and callers should not retry again.
    ///     Unexpected (unclassified) failures are not wrapped as <see cref="WorkerJobSourceException" />
    ///     and should surface to callers as the raw underlying exception.
    /// </exception>
    Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default);

    Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Extend the in-flight / visibility window for a job record, if the underlying message source supports it.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="WorkerJobSourceException">
    ///     Thrown when the heartbeat / visibility extension fails against the underlying message source.
    ///     When <see cref="WorkerJobSourceException.CouldBeTransient" /> is <c>true</c>, callers may retry;
    ///     when <c>false</c>, the failure should be treated as permanent (for example, the message can no longer
    ///     have its flight time extended).
    ///     When <see cref="WorkerJobSourceException.IsHandled" /> is <c>true</c>, a job-source retry wrapper
    ///     has already exhausted retries and callers should not retry again.
    ///     Unexpected (unclassified) failures are not wrapped as <see cref="WorkerJobSourceException" />
    ///     and should surface to callers as the raw underlying exception.
    /// </exception>
    Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Start a subscriber that delivers jobs from this source into the worker.
    ///     Supported only when <see cref="IsSubscriptionSource" /> is <c>true</c>.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <see cref="IsSubscriptionSource" /> is <c>false</c>.
    /// </exception>
    Task StartSubscriberAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stop a subscriber previously started by <see cref="StartSubscriberAsync" />.
    ///     Supported only when <see cref="IsSubscriptionSource" /> is <c>true</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <see cref="IsSubscriptionSource" /> is <c>false</c>.
    /// </exception>
    void StopSubscriber();
}