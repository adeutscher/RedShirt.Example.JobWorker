using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.Core.Utility;
using System.Collections.Concurrent;

namespace RedShirt.Example.JobWorker.Core.Services;

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

    Task LoadAsync(JobSourceResponse jobSourceResponse,
        CancellationToken cancellationToken = default);

    Task ReloadUnblockedJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default);

    Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default);

    Task WaitForEmptyRepositoryAsync(CancellationToken cancellationToken = default);

    Task<bool> WaitForJobDemandAsync(TimeSpan waitDuration, CancellationToken cancellationToken = default);
}

internal class JobRepository(
    IExecutionEndArbiter executionEndArbiter,
    IJobLoaderStateService jobLoaderStateService,
    ISourceMessageSorter sorter,
    IOptions<JobRepository.ConfigurationModel> options)
    : IJobRepository
{
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
    ///     Signaled when the repository has no watched jobs.
    ///     Starts signaled because the repository begins empty.
    /// </summary>
    private readonly AsyncManualResetEvent _repositoryEmptyEvent = new(true);

    /// <summary>
    ///     Jobs that have recently been unblocked due to an idempotency lock.
    ///     This queue intended as a shortlist that will jump the normal sorted line of the inactive jobs list.
    /// </summary>
    private readonly ConcurrentQueue<IJobRepositoryEntry> _unblockedJobsQueue = new();

    private readonly SemaphoreSlim _watchedJobsListSemaphore = new(1, 1);

    /// <summary>
    ///     Inactive potential jobs
    ///     Reminder: This is currently a list instead of a queue because it needs to be sorted in a manner that is consistent
    ///     with the Batch approach
    ///     Similarly, confirming that it is intentional that this list not be marked as readonly.
    /// </summary>
    private List<IJobRepositoryEntry> _inactiveJobsList = [];

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
        IJobRepositoryEntry? result;
        do
        {
            // Try shortlist of unblocked jobs
            if (_unblockedJobsQueue.TryDequeue(out result))
            {
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

                // Continue out of loop iteration to abort via do-while condition
                continue;
            }

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

                    // Continue out of loop iteration to abort via do-while condition
                    continue;
                }
            }
            finally
            {
                _inactiveJobsListSemaphore.Release();
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

        await result.SetStateAsync(JobState.Active, cancellationToken);

        return result;
    }

    public async Task LoadAsync(JobSourceResponse jobSourceResponse,
        CancellationToken cancellationToken = default)
    {
        if (jobSourceResponse.Items.Count == 0)
        {
            // If nothing to add, then don't bother with semaphores
            return;
        }

        await _inactiveJobsListSemaphore.WaitAsync(cancellationToken);

        try
        {
            await _watchedJobsListSemaphore.WaitAsync(cancellationToken);

            try
            {
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var jobModel in jobSourceResponse.Items)
                {
                    var job = new JobRepositoryEntry
                    {
                        LastHeartbeatTime = DateTime.UtcNow,
                        JobModel = jobModel
                    };

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
    }

    public Task ReloadUnblockedJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default)
    {
        job.SetStateAsync(JobState.Inactive, cancellationToken);
        // Shortlist the job for re-execution in memory
        _unblockedJobsQueue.Enqueue(job);
        // Tell any active invocations of GetNextJobAsync that there is something available.
        _jobsAvailableEvent.Set();
        return Task.CompletedTask;
    }

    public async Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default)
    {
        await _watchedJobsListSemaphore.WaitAsync(cancellationToken);
        try
        {
            WatchedJobs.Remove(job);

            if (WatchedJobs.Count == 0)
            {
                // Avoid possible race condition in JobLoader
                _jobsDemandEvent.Set();
                _repositoryEmptyEvent.Set();
            }
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

    internal sealed class ConfigurationModel
    {
        public required int BacklogSize { get; init; }
        public int EffectiveBacklogSize => Math.Max(0, BacklogSize);
    }
}