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

/// <summary>
///     Subscriber intake queue implementation. Probably a bit more thread-safe than it really needs to be...
/// </summary>
internal class JobSubscriberIntakeQueue : IJobSubscriberIntakeQueue
{
    private readonly ConcurrentQueue<IJobSourceResponse> _jobs = new();
    private readonly AsyncManualResetEvent _jobsAreAvailableIfSetEvent = new();
    private readonly Lock _lock = new();
    private bool _done;

    /// <summary>
    ///     Like in other places in this codebase, using a bool out of complete paranoia of redundant Set/Resets having an
    ///     impact.
    /// </summary>
    private bool _jobsAreAvailableIfSetEventIsSet;

    private void Cancel()
    {
        lock (_lock)
        {
            // Mark as done
            _done = true;
            _jobsAreAvailableIfSetEvent.Set();
        }
    }

    /// <summary>
    ///     Update event state. Assumed to be run behind a lock.
    /// </summary>
    private void UpdateEventUnsafe()
    {
        if (_done)
        {
            // Already done, keep event in locked-in state.
            return;
        }

        if (_jobs.IsEmpty)
        {
            // ReSharper disable once InvertIf
            if (_jobsAreAvailableIfSetEventIsSet)
            {
                _jobsAreAvailableIfSetEvent.Reset();
                _jobsAreAvailableIfSetEventIsSet = false;
            }
        }
        else
        {
            // ReSharper disable once InvertIf
            if (!_jobsAreAvailableIfSetEventIsSet)
            {
                _jobsAreAvailableIfSetEvent.Set();
                _jobsAreAvailableIfSetEventIsSet = true;
            }
        }
    }

    public JobSubscriberIntakeQueue(IExecutionEndArbiter executionEndArbiter)
    {
        executionEndArbiter.AddOnStopCallback(_ => Cancel());
    }

    public void Load(IJobSourceResponse jobSourceResponse)
    {
        lock (_lock)
        {
            _jobs.Enqueue(jobSourceResponse);
            UpdateEventUnsafe();
        }
    }

    public async Task<IJobSourceResponse?> GetNextAsync(CancellationToken cancellationToken = default)
    {
        IJobSourceResponse? response;
        while (true)
        {
            // Wait until there are jobs available.
            // ReSharper disable once InconsistentlySynchronizedField
            await _jobsAreAvailableIfSetEvent.WaitAsync(cancellationToken);

            lock (_lock)
            {
                try
                {
                    if (_jobs.TryDequeue(out response))
                    {
                        break;
                    }
                }
                finally
                {
                    UpdateEventUnsafe();
                }

                if (_done)
                {
                    // Return null, indicating to consumers that things are done
                    break;
                }
            }
        }

        return response;
    }
}