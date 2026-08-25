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
internal interface IJobRepository : IDisposable
{
    Task<List<IJobRepositoryEntry>> GetAllIdempotencyBlockedJobsAsync(CancellationToken cancellationToken = default);
    Task<List<IJobRepositoryEntry>> GetAllInFlightJobsAsync(CancellationToken cancellationToken = default);
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

    Task WaitForJobDemandAsync(CancellationToken cancellationToken = default);
}

internal sealed class JobRepository : IJobRepository
{
    private readonly Lock _callbackLock = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly Lock _generalLock = new();
    private readonly SemaphoreSlim _inactiveJobsListSemaphore = new(1, 1);
    private readonly IJobLoaderStateReaderService _jobLoaderStateService;

    /// <summary>
    ///     Indicates that jobs are available to be pulled by JobExecutor instances via GetNextJobAsync.
    /// </summary>
    private readonly AsyncManualResetEvent _jobsAvailableEvent = new();

    /// <summary>
    ///     Guards Set/Reset of <see cref="_jobsAvailableEvent" /> together with enqueue onto
    ///     <see cref="_unblockedJobsQueue" /> and <see cref="_inactiveJobsList" />.
    ///     Lock order: <see cref="_inactiveJobsListSemaphore" /> then this gate.
    /// </summary>
    private readonly Lock _jobsAvailableLock = new();

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

    /// <summary>
    ///     Guards Set/Reset of <see cref="_repositoryEmptyEvent" />.
    /// </summary>
    private readonly Lock _repositoryEmptyLock = new();

    private readonly ISourceMessageSorter _sorter;

    private readonly Lock _tallyLock = new();

    /// <summary>
    ///     Jobs that have recently been unblocked due to an idempotency lock.
    ///     This queue intended as a shortlist that will jump the normal sorted line of the inactive jobs list.
    /// </summary>
    private readonly ConcurrentQueue<IJobRepositoryEntry> _unblockedJobsQueue = new();

    private readonly SemaphoreSlim _watchedJobsListSemaphore = new(1, 1);

    private bool _disposed;

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

    /// <summary>
    ///     Notes if <see cref="_jobsAvailableEvent" /> is set.
    ///     Created because of optimization paranoia to avoid unnecessary event sets/resets to
    ///     <see cref="_jobsAvailableEvent" />.
    ///     Use should be gated behind <see cref="_jobsAvailableLock" />.
    /// </summary>
    private bool _jobsAvailableEventIsSet;

    /// <summary>
    ///     Notes if <see cref="_repositoryEmptyEvent" /> is set.
    ///     Created because of optimization paranoia to avoid unnecessary event sets/resets to
    ///     <see cref="_repositoryEmptyEvent" />.
    ///     Use should be gated behind <see cref="_repositoryEmptyLock" />.
    /// </summary>
    private bool _repositoryEmptyEventIsSet = true;

    private Action<int>? _watchedJobsCallbacks;
    private int _watchedJobsTally;

    private void OnExecutionEndArbiterStop(Exception? exception)
    {
        ConsiderInterruptingEventWaits();
    }

    /// <summary>
    ///     Check to see if we should cancel the local CancellationTokenSource to interrupt method invocations that are waiting
    ///     on an event.
    /// </summary>
    private void ConsiderInterruptingEventWaits()
    {
        lock (_generalLock)
        {
            if (_disposed)
            {
                return;
            }

            if (!HaveReasonToExpectFutureJobs())
            {
                _cancellationTokenSource.Cancel();
            }
        }
    }

    private void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_generalLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

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

    /// <summary>
    ///     Align <see cref="_jobsAvailableEvent" /> with whether inactive jobs or shortlisted jobs exist.
    ///     Assumed to run while holding <see cref="_inactiveJobsListSemaphore" />.
    /// </summary>
    private void SyncRepositoryEmptyEvent()
    {
        lock (_repositoryEmptyLock)
        {
            bool isEmptyCondition;
            lock (_tallyLock)
            {
                isEmptyCondition = _watchedJobsTally == 0;
            }

            if (isEmptyCondition)
            {
                // Job list is empty

                // ReSharper disable once InvertIf
                if (!_repositoryEmptyEventIsSet)
                {
                    _repositoryEmptyEvent.Set();
                    _repositoryEmptyEventIsSet = true;
                }
            }
            else if (_repositoryEmptyEventIsSet)
            {
                _repositoryEmptyEvent.Reset();
                _repositoryEmptyEventIsSet = false;
            }
        }
    }

    /// <summary>
    ///     Align <see cref="_jobsAvailableEvent" /> with whether inactive jobs or shortlisted jobs exist.
    ///     Should be run after state has been updated and a data store has been updated.
    /// </summary>
    private void SyncJobsAvailableEvent()
    {
        lock (_jobsAvailableLock)
        {
            bool isEmptyCondition;
            lock (_tallyLock)
            {
                isEmptyCondition = _inactiveJobsTally == 0;
            }

            isEmptyCondition &= _unblockedJobsQueue.IsEmpty;

            if (isEmptyCondition)
            {
                // Job list is empty

                // ReSharper disable once InvertIf
                if (_jobsAvailableEventIsSet)
                {
                    _jobsAvailableEvent.Reset();
                    _jobsAvailableEventIsSet = false;
                }
            }
            else if (!_jobsAvailableEventIsSet)
            {
                _jobsAvailableEvent.Set();
                _jobsAvailableEventIsSet = true;
            }
        }
    }

    private TryGetJobResponse TryGetUnblockedJobAsync()
    {
        IJobRepositoryEntry? result;
        var iterated = false;

        // ReSharper disable once InconsistentlySynchronizedField
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

        // Need to set state before thinking about syncing events
        result?.State = JobState.Active;

        if (iterated)
        {
            SyncJobsAvailableEvent();
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
            }
        }
        finally
        {
            _inactiveJobsListSemaphore.Release();
        }

        // Need to set state before thinking about syncing events
        result?.State = JobState.Active;

        if (result is not null)
        {
            SyncJobsAvailableEvent();
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

        lock (_jobsAvailableLock)
        {
            // Identified as a newly-unblocked job.
            // Shortlist the job for re-execution in memory.
            _unblockedJobsQueue.Enqueue(job);
            // Tell any active invocations of GetNextJobAsync that there is something available.
            _jobsAvailableEvent.Set();
        }
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

        var consideringToConsiderCancellingWaitEvents = false; // Yes, this variable name is very silly
        if (updatedInactive)
        {
            NotifyInactiveCountUpdate(localTallyInactive);
            consideringToConsiderCancellingWaitEvents |= localTallyInactive == 0;
        }

        if (updatedIdempotencyBlocked)
        {
            NotifyIdempotencyBlockedCountUpdate(localTallyIdempotencyBlocked);
            consideringToConsiderCancellingWaitEvents |= localTallyIdempotencyBlocked == 0;
        }

        if (updatedWatched)
        {
            NotifyWatchedJobsUpdate(localTallyWatched);
        }

        if (consideringToConsiderCancellingWaitEvents)
        {
            ConsiderInterruptingEventWaits();
        }

        /*
         * Note: Although tallies are updated here, SyncJobsAvailableEvent should not be invoked here.
         * SyncJobsAvailableEvent reads off these tallies that suggest a state, but in practice the events are used
         *  for more concrete realities and tallies are set before these realities are implemented (read: before the
         *  inactive jobs list or unblocked jobs queue is updated). Therefore, SyncJobsAvailableEvent should only be
         *  invoked when these sources of truth have been updated.
         */
    }

    private bool HaveReasonToExpectFutureJobs()
    {
        if (
            // If execution is still running, then we have every reason to believe that there will be more incoming jobs.
            // Note: Using the raw IExecutionEndArbiter because we want to avoid a circular dependency.
            _executionEndArbiter.ShouldKeepRunning()
            // If the job loader is not yet finished, then there may be more incoming jobs.
            || !_jobLoaderStateService.IsLoaderFinished())
        {
            return true;
        }

        lock (_tallyLock)
        {
            // Confirm whether there are any inactive jobs, or jobs that may become inactive again. 
            return _inactiveJobsTally > 0
                   || _idempotencyBlockedTally > 0;
        }
    }

    private async Task DoOperationWithLinkedToken(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Construct a linked CTS to tie it to _cancellationTokenSource.
         *
         * Note: During development, I experimented with a GetLinkedToken method,
         * but that ended up being invalid. The reason it was invalid is that
         *  disposing a linked source unregisters it from _cancellationTokenSource.
         *  The CancellationToken that was being returned was no longer hooked to that source,
         *  making the end result just the baseline cancellation token with extra steps.
         */

        CancellationTokenSource? linkedCts = null;
        try
        {
            CancellationToken linkedToken;
            lock (_generalLock)
            {
                if (_disposed)
                {
                    throw new OperationCanceledException();
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token);
                linkedToken = linkedCts.Token;
            }

            await operation(linkedToken);
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    private Task DoWaitForAvailableJobsAsync(CancellationToken cancellationToken)
    {
        return _jobsAvailableEvent.WaitAsync(cancellationToken);
    }

    private Task DoWaitForEmptyRepositoryAsync(CancellationToken cancellationToken)
    {
        return _repositoryEmptyEvent.WaitAsync(cancellationToken);
    }

    public JobRepository(IExecutionEndArbiter executionEndArbiter,
        IJobLoaderStateReaderService jobLoaderStateReaderService,
        ISourceMessageSorter sourceMessageSorter,
        IOptions<ConfigurationModel> _)
    {
        _executionEndArbiter = executionEndArbiter;
        _jobLoaderStateService = jobLoaderStateReaderService;
        _sorter = sourceMessageSorter;

        executionEndArbiter.AddOnStopCallback(OnExecutionEndArbiterStop);
        jobLoaderStateReaderService.AddOnFinishCallback(ConsiderInterruptingEventWaits);
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

        int count;
        try
        {
            count = WatchedJobs.Count;
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }

        return count;
    }

    public async Task<IJobRepositoryEntry?> GetNextJobAsync(CancellationToken cancellationToken = default)
    {
        IJobRepositoryEntry? result = null;
        do
        {
            // Try shortlist of unblocked jobs
            if (TryGetUnblockedJobAsync() is {Success: true, Result: { } formerlyUnblockedJobResult})
            {
                result = formerlyUnblockedJobResult;

                // Continue out of loop iteration to abort via do-while condition
                continue;
            }

            if (await TryGetInactiveJobAsync(cancellationToken) is
                {Success: true, Result: { } formerlyInactiveJobResult})
            {
                result = formerlyInactiveJobResult;
                // Continue out of loop iteration to abort via do-while condition
                continue;
            }

            // If execution has reached here, then there are currently no available jobs to be handed out.

            // Is it because we've been asked to stop running?
            if (!HaveReasonToExpectFutureJobs())
            {
                // It IS because we've been asked to stop running!
                // We have also confirmed that the job loader is fully finished, and no more jobs are incoming 
                return null;
            }

            // Note that there's a demand.
            // Only the loader mode should care about this via the IJobRepository.WaitForJobDemandAsync method
            _jobsDemandEvent.Set();

            try
            {
                await DoOperationWithLinkedToken(DoWaitForAvailableJobsAsync, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Exception from a cancelled internal CTS suggests shutdown
                // Manually break from loop so that invoking executors can abort
                break;
            }
        } while (result is null);

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
            _inactiveJobsList = _sorter.GetSortedListOfJobs(_inactiveJobsList);
            SyncJobsAvailableEvent();
            SyncRepositoryEmptyEvent();
        }
        finally
        {
            _inactiveJobsListSemaphore.Release();
        }

        NotifyWatchedJobsUpdate(await GetWatchedJobsCountAsync(cancellationToken));
    }

    public async Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await _inactiveJobsListSemaphore.WaitAsync(cancellationToken);
        try
        {
            _inactiveJobsList.Remove(job);
            SyncJobsAvailableEvent();
        }
        finally
        {
            _inactiveJobsListSemaphore.Release();
        }

        var watchedIsNowEmpty = false;
        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (WatchedJobs.Remove(job) && WatchedJobs.Count == 0)
            {
                // Just reached zero.
                // Avoid possible race condition in JobLoader

                // If there's nothing to grab, then there must be an executor about to demand something. 
                _jobsDemandEvent.Set();
                // No watched jobs is the very definition of an empty repository
                watchedIsNowEmpty = true;
            }
        }
        finally
        {
            _watchedJobsListSemaphore.Release();
        }

        job.Dispose();
        if (watchedIsNowEmpty)
        {
            SyncRepositoryEmptyEvent();
        }
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

    public Task WaitForJobDemandAsync(CancellationToken cancellationToken = default)
    {
        return DoOperationWithLinkedToken(
            linkedToken => _jobsDemandEvent.WaitAsync(linkedToken),
            cancellationToken);
    }

    public async Task WaitForEmptyRepositoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await DoOperationWithLinkedToken(DoWaitForEmptyRepositoryAsync, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Pass
        }
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

    public void Dispose()
    {
        Dispose(true);
        // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
        GC.SuppressFinalize(this);
    }

    private sealed class TryGetJobResponse
    {
        public required bool Success { get; init; }
        public required IJobRepositoryEntry? Result { get; init; }
    }

    internal sealed class ConfigurationModel
    {
        public required int BacklogSize { get; init; }
    }
}