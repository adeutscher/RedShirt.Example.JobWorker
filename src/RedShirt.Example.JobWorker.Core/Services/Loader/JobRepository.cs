using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Models.Loader;

namespace RedShirt.Example.JobWorker.Core.Services.Loader;

/// <summary>
///     The Job Repository is the central storage location for Loader-style job management.
/// </summary>
internal interface IJobRepository
{
    Task<List<IJobRepositoryEntry>> GetAllInFlightJobsAsync(CancellationToken cancellationToken = default);
    int GetBacklogMaxCount();
    Task<int> GetInactiveJobCountAsync(CancellationToken cancellationToken = default);
    Task<IJobRepositoryEntry?> GetNextJobAsync(CancellationToken cancellationToken = default);
    Task<int> GetWatchedJobsCountAsync(CancellationToken cancellationToken = default);

    Task LoadAsync(JobSourceResponse jobSourceResponse,
        CancellationToken cancellationToken = default);

    Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default);

    Task WaitForJobDemandAsync(CancellationToken cancellationToken = default);
}

internal class JobRepository(
    IExecutionEndArbiter executionEndArbiter,
    ISourceMessageSorter sorter,
    ILogger<JobRepository> logger,
    IOptions<JobRepository.ConfigurationModel> options)
    : IJobRepository
{
    private readonly SemaphoreSlim _inactiveJobsSemaphore = new(1, 1);

    private readonly ManualResetEvent _jobsArrivedEvent = new(false);

    private readonly ManualResetEvent _jobsDemandEvent = new(false);
    private readonly SemaphoreSlim _watchedJobsSemaphore = new(1, 1);
    private List<IJobRepositoryEntry> _inactiveJobs = new();
    internal List<IJobRepositoryEntry> WatchedJobs { get; } = new();

    public async Task<List<IJobRepositoryEntry>> GetAllInFlightJobsAsync(CancellationToken cancellationToken = default)
    {
        await _watchedJobsSemaphore.WaitAsync(cancellationToken);

        var items = WatchedJobs
            .Where(job => job.State is JobState.Inactive or JobState.Active)
            .ToList();

        _watchedJobsSemaphore.Release();

        return items;
    }

    public async Task<int> GetWatchedJobsCountAsync(CancellationToken cancellationToken = default)
    {
        await _watchedJobsSemaphore.WaitAsync(cancellationToken);

        var count = WatchedJobs.Count;

        _watchedJobsSemaphore.Release();

        return count;
    }

    public async Task<IJobRepositoryEntry?> GetNextJobAsync(CancellationToken cancellationToken = default)
    {
        IJobRepositoryEntry? result;
        do
        {
            await _inactiveJobsSemaphore.WaitAsync(cancellationToken);
            result = _inactiveJobs.FirstOrDefault();
            _inactiveJobsSemaphore.Release();

            if (result is not null)
            {
                logger.LogTrace("Received job out of queue");
                continue;
            }

            // Queue is currently empty

            // Is it because we've been asked to stop running?
            if (!executionEndArbiter.ShouldKeepRunning())
            {
                // It IS because we've been asked to stop running!
                return null;
            }

            // Note that there's a demand.
            // Only the JobLoader should care about this via the IJobRepository.WaitForJobDemandAsync
            _jobsDemandEvent.Set();

            // Wait for jobs to arrive
            // The milliseconds timeout is necessary due to timing problems that came up during unit testing
            // I can't say that I'm thrilled with it, though...
            _jobsArrivedEvent.WaitOne(250);
        } while (result is null);

        return result;
    }

    public async Task LoadAsync(JobSourceResponse jobSourceResponse,
        CancellationToken cancellationToken = default)
    {
        await _watchedJobsSemaphore.WaitAsync(cancellationToken);
        await _inactiveJobsSemaphore.WaitAsync(cancellationToken);
        foreach (var jobModel in jobSourceResponse.Items)
        {
            var job = new JobRepositoryEntry
            {
                FlightTimeCanBeExtended = true,
                LastHeartbeatTime = DateTime.UtcNow,
                JobModel = jobModel,
                State = JobState.Inactive
            };

            _inactiveJobs.Add(job); // Worry about sorting later, see below

            _jobsDemandEvent.Reset();
            WatchedJobs.Add(job);
        }

        _watchedJobsSemaphore.Release();

        /*
         * Refresh list
         *
         * There's probably more efficient ways to insert, but:
         * * Needs to be compatible with Batch mode, at least for the time being.
         * * We're assuming that we're not working with enormous datasets for our backlog size.
         */
        _inactiveJobs = sorter.GetSortedListOfJobs(_inactiveJobs);
        _inactiveJobsSemaphore.Release();

        _jobsArrivedEvent.Set();
        _jobsArrivedEvent.Reset();
    }

    public async Task RemoveJobAsync(IJobRepositoryEntry job, CancellationToken cancellationToken = default)
    {
        await _watchedJobsSemaphore.WaitAsync(cancellationToken);

        WatchedJobs.Remove(job);

        if (WatchedJobs.Count == 0)
        {
            // Avoid possible race condition in JobLoader
            _jobsDemandEvent.Set();
        }

        _watchedJobsSemaphore.Release();
    }

    public Task WaitForJobDemandAsync(CancellationToken cancellationToken = default)
    {
        _jobsDemandEvent.WaitOne();
        return Task.CompletedTask;
    }

    public int GetBacklogMaxCount()
    {
        return options.Value.EffectiveBacklogSize;
    }

    public async Task<int> GetInactiveJobCountAsync(CancellationToken cancellationToken = default)
    {
        await _watchedJobsSemaphore.WaitAsync(cancellationToken);

        // Note for future: Possibly need to review use of lock? Caution is good, but on the other hand I'm only reading...
        var count = WatchedJobs.Count(job => job.State == JobState.Inactive);

        _watchedJobsSemaphore.Release();

        return count;
    }

    internal sealed class ConfigurationModel
    {
        public required int BacklogSize { get; init; }
        public int EffectiveBacklogSize => Math.Max(0, BacklogSize);
    }
}