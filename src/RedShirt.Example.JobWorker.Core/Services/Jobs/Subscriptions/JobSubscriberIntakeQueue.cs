using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Utility;
using System.Collections.Concurrent;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

/// <summary>
///     In-memory handoff queue between a subscription job source and <see cref="JobSubscriberManager" />.
///     Subscription sources push batches via <see cref="Load" />; the subscriber manager drains them via
///     <see cref="GetNextAsync" /> and submits each batch through job intake.
///     This subscriber queue should not be used in a non subscriber context. If the configured <see cref="IJobSource" />
///     is not a subscriber, then this queue will not be read from.
///     This interface exists because <see cref="JobIntakeService" /> indirectly uses
///     <see cref="IJobSource" /> as a dependency. Creating this queue was the most expedient way to avoid a circular loop.
/// </summary>
public interface IJobSubscriberIntakeQueue
{
    /// <summary>
    ///     Wait for the next loaded job-source response.
    ///     Returns the next enqueued response when one is available.
    ///     Returns <c>null</c> once the worker is stopping and no further responses remain in the queue,
    ///     signalling that the subscriber manager should finish draining and exit.
    /// </summary>
    /// <param name="cancellationToken">
    ///     Cancels the wait when the caller is aborting independently of worker shutdown.
    /// </param>
    /// <returns>
    ///     The next <see cref="IJobSourceResponse" />, or <c>null</c> when shutdown has completed the queue.
    /// </returns>
    Task<IJobSourceResponse?> GetNextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Enqueue a job-source response for later consumption by <see cref="GetNextAsync" />.
    ///     Intended to be called from subscription delivery callbacks on the job source.
    /// </summary>
    /// <param name="jobSourceResponse">The batch of jobs delivered by the subscription source.</param>
    void Load(IJobSourceResponse jobSourceResponse);
}

internal class JobSubscriberIntakeQueue : IJobSubscriberIntakeQueue
{
    private readonly AsyncManualResetEvent _jobsAreAvailableIfSetEvent = new();
    private readonly ConcurrentQueue<IJobSourceResponse> _jobs = new();
    private bool _done;

    private void Cancel()
    {
        _done = true;
        _jobsAreAvailableIfSetEvent.Set();
    }

    public JobSubscriberIntakeQueue(IExecutionEndArbiter executionEndArbiter)
    {
        executionEndArbiter.AddOnStopCallback(_ => Cancel());
    }

    public void Load(IJobSourceResponse jobSourceResponse)
    {
        _jobs.Enqueue(jobSourceResponse);
        _jobsAreAvailableIfSetEvent.Set();
    }

    public async Task<IJobSourceResponse?> GetNextAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _jobsAreAvailableIfSetEvent.WaitAsync(cancellationToken);
            if (_jobs.TryDequeue(out var jobSourceResponse))
            {
                return jobSourceResponse;
            }

            if (_done)
            {
                // Return null, indicating to consumers that things are done
                return null;
            }

            // If we are not done, then indicate a demand.
            _jobsAreAvailableIfSetEvent.Reset();
        }
    }
}