using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.Core.Utility;
using System.Collections.Concurrent;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs;

/// <summary>
///     The Job Repository is the central storage location for in-memory job management.
///     Note: This interface and its implementation was originally made specifically for Loader mode, but was moved to be
///     general-use when Batch mode was refactored.
///     If you choose to use one polling mode when applying this template by pruning the other one, then you may want to
///     prune in this method as well.
/// </summary>
internal interface IJobRepository
{
    Task<List<IJobRepositoryEntry>> GetAllIdempotencyBlockedJobsAsync(CancellationToken cancellationToken = default);
    Task<List<IJobRepositoryEntry>> GetAllInFlightJobsAsync(CancellationToken cancellationToken = default);
    int GetBacklogMaxCount();
    Task<int> GetInactiveJobCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Wait until the next job is available for execution.
    ///     If there is no available job for execution and the application is shutting down, then this will return null.
    ///     Otherwise, it should return a non-null value.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IJobRepositoryEntry?> GetNextJobAsync(CancellationToken cancellationToken = default);

    Task<int> GetWatchedJobsCountAsync(CancellationToken cancellationToken = default);

    Task LoadAsync(IReadOnlyList<IJobEnvelope> intakeItems,
        CancellationToken cancellationToken = default);

    Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Register a callback invoked with the current count of jobs blocked by idempotency whenever
    ///     that count changes via repository operations. Invoked immediately with the current count on subscribe.
    /// </summary>
    void SubscribeToIdempotencyBlockedCountUpdate(Action<int> callback);

    /// <summary>
    ///     Register a callback invoked with the current inactive-job count whenever that count changes
    ///     via repository operations. Invoked immediately with the current count on subscribe.
    /// </summary>
    void SubscribeToInactiveCountUpdate(Action<int> callback);

    /// <summary>
    ///     Register a callback invoked with the current watched-job count whenever that count changes
    ///     via repository operations. Invoked immediately with the current count on subscribe.
    /// </summary>
    void SubscribeToWatchedJobsUpdate(Action<int> callback);

    Task WaitForEmptyRepositoryAsync(CancellationToken cancellationToken = default);

    Task<bool> WaitForJobDemandAsync(TimeSpan waitDuration, CancellationToken cancellationToken = default);
}

internal sealed class JobRepository(
    IExecutionEndArbiter executionEndArbiter,
    IJobLoaderStateReaderService jobLoaderStateService,
    ISourceMessageSorter sorter,
    IOptions<JobRepository.ConfigurationModel> options)
    : IJobRepository
{
    private readonly Lock _callbackLock = new();
    private readonly SemaphoreSlim _inactiveJobsListSemaphore = new(1, 1);

    /// <summary>
    ///     Indicates that jobs are available to be pulled by JobExecutor instances via GetNextJobAsync.
    /// </summary>
    private readonly AsyncManualResetEvent _jobsAvailableEvent = new();

    /// <summary>
    ///     Signalled when the repository has no watched jobs OR the repository was unable to produce an inactive job for a
    ///     worker request.
    /// </summary>
    private readonly AsyncManualResetEvent _jobsDemandEvent = new();

    /// <summary>
    ///     Signalled when the repository has no watched jobs.
    ///     Starts signalled because the repository begins empty.
    /// </summary>
    private readonly AsyncManualResetEvent _repositoryEmptyEvent = new(true);

    private readonly Lock _tallyLock = new();

    /// <summary>
    ///     Jobs that have recently been unblocked due to an idempotency lock.
    ///     This queue intended as a shortlist that will jump the normal sorted line of the inactive jobs list.
    /// </summary>
    private readonly ConcurrentQueue<IJobRepositoryEntry> _unblockedJobsQueue = new();

    private readonly SemaphoreSlim _watchedJobsListSemaphore = new(1, 1);

    private Action<int>? _idempotencyBlockedJobsCallbacks;

    private int _idempotencyBlockedTally;

    private Action<int>? _inactiveCountCallbacks;

    /// <summary>
    ///     Inactive potential jobs.
    ///     Reminder: This is currently a list instead of a queue because it needs to be sorted in a manner that is consistent
    ///     with the Batch approach.
    ///     Similarly, confirming that it is intentional that this list not be marked as readonly.
    /// </summary>
    private List<IJobRepositoryEntry> _inactiveJobsList = [];

    private int _inactiveJobsTally;

    private Action<int>? _watchedJobsCallbacks;
    private int _watchedJobsTally;

    private void NotifyInactiveCountUpdate(int count)
    {
        Action<int>? callbacks;
        lock (_callbackLock)
        {
            callbacks = _inactiveCountCallbacks;
        }

        callbacks?.Invoke(count);
    }

    private void NotifyIdempotencyBlockedCountUpdate(int count)
    {
        Action<int>? callbacks;
        lock (_callbackLock)
        {
            callbacks = _idempotencyBlockedJobsCallbacks;
        }

        callbacks?.Invoke(count);
    }

    private void NotifyWatchedJobsUpdate(int count)
    {
        Action<int>? callbacks;
        lock (_callbackLock)
        {
            callbacks = _watchedJobsCallbacks;
        }

        callbacks?.Invoke(count);
    }

    private async Task<TryGetJobResponse> TryGetUnblockedJobAsync(CancellationToken cancellationToken)
    {
        IJobRepositoryEntry? result;
        var iterated = false;

        while (_unblockedJobsQueue.TryDequeue(out result))
        {
            iterated = true;
            // Handle potential edge case of something disposing an item that was recently unblocked
            // This absolutely should not happen, but at least it won't mess things up further if it does.

            if (!result.IsDisposed)
            {
                break;
            }
        }

        if (iterated)
        {
            // Check to see if we emptied the queue, but only if we actually dequeued something
            // Assume that the event is up to date and doesn't need a redundant reset.
            await _inactiveJobsListSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_inactiveJobsList.Count == 0 && _unblockedJobsQueue.IsEmpty)
                {
                    // Jobs are no longer available
                    _jobsAvailableEvent.Reset();
                }
            }
            finally
            {
                _inactiveJobsListSemaphore.Release();
            }
        }

        if (result is null
            // Account for technical race condition, will never happen in practice
            || result.IsDisposed)
        {
            return new TryGetJobResponse
            {
                Success = false,
                Result = null
            };
        }

        return new TryGetJobResponse
        {
            Success = true,
            Result = result
        };
    }

    private async Task<TryGetJobResponse> TryGetInactiveJobAsync(CancellationToken cancellationToken)
    {
        IJobRepositoryEntry? result;
        await _inactiveJobsListSemaphore.WaitAsync(cancellationToken);
        try
        {
            result = _inactiveJobsList.FirstOrDefault();

            if (result is not null)
            {
                _inactiveJobsList.RemoveAt(0);

                if (_inactiveJobsList.Count == 0 && _unblockedJobsQueue.IsEmpty)
                {
                    // Jobs are no longer available
                    _jobsAvailableEvent.Reset();
                }
            }
        }
        finally
        {
            _inactiveJobsListSemaphore.Release();
        }

        return new TryGetJobResponse
        {
            Success = result is not null,
            Result = result
        };
    }

    /// <summary>
    ///     Specifically handle transition from idempotency-blocked back to inactive.
    /// </summary>
    /// <param name="job"></param>
    /// <param name="oldState"></param>
    /// <param name="newState"></param>
    private void OnEntryStateUpdateUnblocked(IJobRepositoryEntry job, JobState? oldState, JobState newState)
    {
        if (oldState != JobState.BlockedByIdempotency || newState != JobState.Inactive)
        {
            return;
        }

        // Identified as a newly-unblocked job.
        // Shortlist the job for re-execution in memory.
        _unblockedJobsQueue.Enqueue(job);
        // Tell any active invocations of GetNextJobAsync that there is something available.
        _jobsAvailableEvent.Set();
    }

    /// <summary>
    ///     Handle tally management when an entry changes state.
    /// </summary>
    /// <param name="job"></param>
    /// <param name="oldState"></param>
    /// <param name="newState"></param>
    private void OnEntryStateUpdateTallies(IJobRepositoryEntry job, JobState? oldState, JobState newState)
    {
        _ = job;

        var updatedWatched = false;
        var updatedInactive = false;
        var updatedIdempotencyBlocked = false;

        var localTallyWatched = 0;
        var localTallyInactive = 0;
        var localTallyIdempotencyBlocked = 0;

        lock (_tallyLock)
        {
            /* Track watched tally */

            if (oldState is not null && newState == JobState.Complete)
            {
                // Moving from watched to unwatched (as opposed to directly to Complete, which would skip watching altogether)
                updatedWatched = true;
                _watchedJobsTally--;
            }
            else if (oldState is null && newState != JobState.Complete)
            {
                // Moving from unwatched to watched (as opposed to directly to Complete)
                updatedWatched = true;
                _watchedJobsTally++;
            }

            /* Track individual tallies */

            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (oldState)
            {
                case JobState.Inactive:
                    _inactiveJobsTally--;
                    updatedInactive = true;
                    break;
                case JobState.BlockedByIdempotency:
                    _idempotencyBlockedTally--;
                    updatedIdempotencyBlocked = true;
                    break;
            }

            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (newState)
            {
                case JobState.Inactive:
                    _inactiveJobsTally++;
                    updatedInactive = true;
                    break;
                case JobState.BlockedByIdempotency:
                    _idempotencyBlockedTally++;
                    updatedIdempotencyBlocked = true;
                    break;
            }

            if (updatedInactive)
            {
                localTallyInactive = _inactiveJobsTally;
            }

            if (updatedIdempotencyBlocked)
            {
                localTallyIdempotencyBlocked = _idempotencyBlockedTally;
            }

            if (updatedWatched)
            {
                localTallyWatched = _watchedJobsTally;
            }
        }

        if (updatedInactive)
        {
            NotifyInactiveCountUpdate(localTallyInactive);
        }

        if (updatedIdempotencyBlocked)
        {
            NotifyIdempotencyBlockedCountUpdate(localTallyIdempotencyBlocked);
        }

        if (updatedWatched)
        {
            NotifyWatchedJobsUpdate(localTallyWatched);
        }
    }

    internal List<IJobRepositoryEntry> WatchedJobs { get; } = [];

    public async Task<List<IJobRepositoryEntry>> GetAllInFlightJobsAsync(CancellationToken cancellationToken = default)
    {
        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);

        try
        {
            var items = WatchedJobs
                .Where(job => job.State is JobState.Inactive or JobState.Active or JobState.BlockedByIdempotency)
                .ToList();

            return items;
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }
    }

    public async Task<List<IJobRepositoryEntry>> GetAllIdempotencyBlockedJobsAsync(
        CancellationToken cancellationToken = default)
    {
        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);

        try
        {
            var items = WatchedJobs
                .Where(job => job.State is JobState.BlockedByIdempotency)
                .ToList();

            return items;
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }
    }

    public async Task<int> GetWatchedJobsCountAsync(CancellationToken cancellationToken = default)
    {
        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);

        var count = WatchedJobs.Count;

        _watchedJobsListSemaphore.Release();

        return count;
    }

    public async Task<IJobRepositoryEntry?> GetNextJobAsync(CancellationToken cancellationToken = default)
    {
        IJobRepositoryEntry? result = null;
        do
        {
            // Try shortlist of unblocked jobs
            if (await TryGetUnblockedJobAsync(cancellationToken) is {Success: true} unblockedAttemptResult)
            {
                result = unblockedAttemptResult.Result!;

                // Continue out of loop iteration to abort via do-while condition
                continue;
            }

            if (await TryGetInactiveJobAsync(cancellationToken) is {Success: true} inactiveAttemptResult)
            {
                result = inactiveAttemptResult.Result!;

                // Continue out of loop iteration to abort via do-while condition
                continue;
            }

            // If execution has reached here, then there are currently no available jobs to be handed out.

            // Is it because we've been asked to stop running?
            if (
                // Note: Using the raw IExecutionEndArbiter because we want to avoid a circular dependency
                !executionEndArbiter.ShouldKeepRunning()
                // Confirm that the job loader thread has finished and will not be loading any more jobs
                && jobLoaderStateService.IsLoaderFinished()
                // Confirm that there are no more jobs in the background.
                // This was already implied by the overall method structure, but now that the loader is finished we want to guarantee it 
                && await GetInactiveJobCountAsync(cancellationToken) == 0)
            {
                // It IS because we've been asked to stop running!
                // We have also confirmed that the job loader is fully finished, and no more jobs are incoming 
                return null;
            }

            // Note that there's a demand.
            // Only the JobLoader should care about this via the IJobRepository.WaitForJobDemandAsync method
            _jobsDemandEvent.Set();

            // Wait for jobs to arrive
            // The milliseconds timeout is necessary due to timing problems that came up during unit testing
            // I can't say that I'm thrilled with it, though...
            await _jobsAvailableEvent.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        } while (result is null);

        result.State = JobState.Active;

        return result;
    }

    public async Task LoadAsync(IReadOnlyList<IJobEnvelope> intakeItems,
        CancellationToken cancellationToken = default)
    {
        if (intakeItems.Count == 0)
        {
            // If nothing to add, then exit right now to avoid bothering with semaphores
            return;
        }

        await _inactiveJobsListSemaphore.WaitAsync(cancellationToken);

        try
        {
            await _watchedJobsListSemaphore.WaitAsync(cancellationToken);

            try
            {
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var envelope in intakeItems)
                {
                    var job = new JobRepositoryEntry
                    {
                        JobModel = envelope.JobModel,
                        RawJobModel = envelope.RawJobModel,
                        LastHeartbeatTime = DateTime.UtcNow,
                        State = JobState.Inactive
                    };
                    job.SubscribeToState(OnEntryStateUpdateTallies);
                    job.SubscribeToState(OnEntryStateUpdateUnblocked);

                    _inactiveJobsList.Add(job); // Worry about sorting later, see below

                    WatchedJobs.Add(job);

                    _jobsDemandEvent.Reset();
                    // Once jobs are added, then the repository is either no longer empty or continues to not be empty.
                    _repositoryEmptyEvent.Reset();
                }
            }
            finally
            {
                _watchedJobsListSemaphore.Release();
            }

            /*
             * Refresh list
             *
             * There's probably more efficient ways to insert, but:
             * * Needs to be compatible with Batch mode, at least for the time being.
             * * We're assuming that we're not working with enormous datasets for our backlog size.
             */
            _inactiveJobsList = sorter.GetSortedListOfJobs(_inactiveJobsList);
        }
        finally
        {
            _inactiveJobsListSemaphore.Release();
        }

        _jobsAvailableEvent.Set();

        NotifyWatchedJobsUpdate(await GetWatchedJobsCountAsync(cancellationToken));
    }

    public async Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await _inactiveJobsListSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_inactiveJobsList.Remove(job)
                && _inactiveJobsList.Count == 0
                && _unblockedJobsQueue.IsEmpty)
            {
                // Jobs are no longer available
                _jobsAvailableEvent.Reset();
            }
        }
        finally
        {
            _inactiveJobsListSemaphore.Release();
        }

        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);
        try
        {
            WatchedJobs.Remove(job);

            if (WatchedJobs.Count == 0)
            {
                // Avoid possible race condition in JobLoader

                // If there's nothing to grab, then there must be an executor about to demand something. 
                _jobsDemandEvent.Set();
                // No watched jobs is the very definition of an empty repository
                _repositoryEmptyEvent.Set();
            }
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }

        job.Dispose();
    }

    public void SubscribeToInactiveCountUpdate(Action<int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _inactiveCountCallbacks += callback;
        }

        lock (_tallyLock)
        {
            callback(_inactiveJobsTally);
        }
    }

    public void SubscribeToIdempotencyBlockedCountUpdate(Action<int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _idempotencyBlockedJobsCallbacks += callback;
        }

        lock (_tallyLock)
        {
            callback(_idempotencyBlockedTally);
        }
    }

    public void SubscribeToWatchedJobsUpdate(Action<int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _watchedJobsCallbacks += callback;
        }

        /*
         * Putting it on the record that I don't particularly like the below implementation on principle.
         * No matter how brief, I'm always twitchy about using a blocking call to a semaphore wait. Probably for no good reason, though.
         */
        _watchedJobsListSemaphore.Wait();
        try
        {
            callback(WatchedJobs.Count);
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }
    }

    public Task<bool> WaitForJobDemandAsync(TimeSpan waitDuration, CancellationToken cancellationToken = default)
    {
        return _jobsDemandEvent.WaitAsync(waitDuration, cancellationToken);
    }

    public async Task WaitForEmptyRepositoryAsync(CancellationToken cancellationToken = default)
    {
        while (await GetWatchedJobsCountAsync(cancellationToken) > 0)
        {
            // Short timeout mirrors GetNextJobAsync: avoids missing a Set/Reset edge under concurrency
            await _repositoryEmptyEvent.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    public int GetBacklogMaxCount()
    {
        return options.Value.EffectiveBacklogSize;
    }

    public async Task<int> GetInactiveJobCountAsync(CancellationToken cancellationToken = default)
    {
        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);

        try
        {
            var count = WatchedJobs.Count(job => job.State == JobState.Inactive);

            return count;
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }
    }

    private sealed class TryGetJobResponse
    {
        public required bool Success { get; init; }
        public required IJobRepositoryEntry? Result { get; init; }
    }

    internal sealed class ConfigurationModel
    {
        public required int BacklogSize { get; init; }
        public int EffectiveBacklogSize => Math.Max(0, BacklogSize);
    }
}