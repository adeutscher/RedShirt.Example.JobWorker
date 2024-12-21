using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface IJobManager
{
    Task RunAsync(JobSourceResponse response, CancellationToken cancellationToken = default);
    void Start(CancellationToken cancellationToken = default);
}

internal class JobManager(
    ISafeJobRunner safeJobRunner,
    IJobSource jobSource,
    ILogger<JobManager> logger,
    IOptions<JobManager.ConfigurationModel> options) : IJobManager
{
    private readonly SemaphoreSlim _completedJobsCountSemaphore = new(1, 1);
    private readonly SemaphoreSlim _completedWorkersCountSemaphore = new(1, 1);

    private readonly ConcurrentQueue<JobEnvelope> _queue = new();

    private readonly ManualResetEvent _readyToReceiveJobsWaitHandle = new(false);
    private readonly SemaphoreSlim _startSemaphore = new(1, 1);

    private readonly List<WaitHandle> _waitHandles = new();
    private readonly AutoResetEvent _workerCompleteEvent = new(false);
    private int _completedJobsCount;

    private int _completedWorkersCount;

    private bool _isLoadingJobs;
    private int _successfullyCompletedJobsCount;

    /// <summary>
    ///     Shorthand method to get thread count.
    /// </summary>
    /// <returns></returns>
    private int GetWorkerCount()
    {
        return Math.Max(1, options.Value.WorkerThreadCount);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken = default)
    {
        var waitHandler = new ManualResetEvent(false);
        await _startSemaphore.WaitAsync(cancellationToken);
        try
        {
            _waitHandles.Add(waitHandler);
        }
        finally
        {
            _startSemaphore.Release();
        }

        while (true)
        {
            waitHandler.Set();
            _readyToReceiveJobsWaitHandle.WaitOne();

            while (true)
            {
                var gotJob = _queue.TryDequeue(out var envelope);
                if (!gotJob && _isLoadingJobs)
                {
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                if (!gotJob)
                {
                    break;
                }

                var result = await safeJobRunner.RunSafelyAsync(envelope!.Job, cancellationToken);

                await _completedJobsCountSemaphore.WaitAsync(cancellationToken);
                try
                {
                    _completedJobsCount++;
                    if (result)
                    {
                        _successfullyCompletedJobsCount++;
                    }
                }
                finally
                {
                    _completedJobsCountSemaphore.Release();
                }

                await envelope.Semaphore.WaitAsync(cancellationToken);
                try
                {
                    envelope.Result = result;
                }
                finally
                {
                    envelope.Semaphore.Release();
                }
            }

            await _completedWorkersCountSemaphore.WaitAsync(cancellationToken);
            try
            {
                _completedWorkersCount++;
                _workerCompleteEvent.Set();
            }
            finally
            {
                _completedWorkersCountSemaphore.Release();
            }
        }
    }

    private async Task HeartbeatMonitorAsync(ManualResetEvent resetEvent, JobSourceResponse sourceResponse,
        List<JobEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        while (envelopes.Count > 0)
        {
            if (resetEvent.WaitOne(TimeSpan.FromSeconds(sourceResponse.RecommendedHeartbeatIntervalSeconds)))
            {
                return;
            }

            for (var i = 0; i < envelopes.Count; i++)
            {
                var item = envelopes[i]; // shorthand
                if (item.IsCompleted)
                {
                    envelopes.RemoveAt(i);
                    i--;
                    continue;
                }

                await item.Semaphore.WaitAsync(cancellationToken);
                try
                {
                    await jobSource.HeartbeatAsync(item.Job, cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogError("Error while running heartbeat: {EMessage}", e.Message);
                    envelopes.RemoveAt(i);
                    i--;
                }
                finally
                {
                    item.Semaphore.Release();
                }
            }
        }
    }

    public async Task RunAsync(JobSourceResponse response, CancellationToken cancellationToken = default)
    {
        // Clear the board
        _successfullyCompletedJobsCount = 0;
        _completedJobsCount = 0;
        _completedWorkersCount = 0;
        var timer = Stopwatch.StartNew();

        // Queue jobs
        _isLoadingJobs = true;

        // Make sure we aren't being asked to manage jobs before the worker threads are ready 
        while (_waitHandles.Count != GetWorkerCount())
        {
            await Task.Delay(1, cancellationToken);
        }
        WaitHandle.WaitAll(_waitHandles.ToArray());
        
        _readyToReceiveJobsWaitHandle.Set();
        _readyToReceiveJobsWaitHandle.Reset();
        
        var envelopes = new List<JobEnvelope>();

        foreach (var item in response.Items)
        {
            var envelope = new JobEnvelope
            {
                Job = item
            };

            _queue.Enqueue(envelope);
            envelopes.Add(envelope);
        }
        
        _isLoadingJobs = false;

        var heartbeatDoneEvent = new ManualResetEvent(false);

        // Monitor heartbeats

        /*
        This must be done in Task.Run because the
        ManualResetEvent.WaitOne call inside blocks
        the entire thread.

        This can be observed in unit tests with
        JobManagerTests.Test_RunJobAsync_Basic_Heartbeat_OneJob_Long,
        which was added specifically to test this situation.

        If run without Task.Run, then the test will fail by way of
        time out because of the blocked thread.

        More subtly, JobManagerTests.Test_RunJobAsync_Basic_Heartbeat_OneJob
        would take over 3 seconds if Task.Run were not used. A clean run
        should take ~2.5 seconds.
         */
        var heartbeatTask = response.RecommendedHeartbeatIntervalSeconds > 0 ?
            Task.Run(() => HeartbeatMonitorAsync(heartbeatDoneEvent, response, envelopes, cancellationToken),
                cancellationToken) : null;

        // Wait for completion

        while (true)
        {
            _workerCompleteEvent.WaitOne(TimeSpan.FromSeconds(1));
            if (_completedWorkersCount == GetWorkerCount())
            {
                break;
            }
        }

        heartbeatDoneEvent.Set();
        if (heartbeatTask is not null)
        {
            await heartbeatTask;
        }

        timer.Stop();
        logger.LogDebug("Successfully finished {JobsSuccessful}/{JobsTotal} jobs in {ElapsedMilliseconds} ms",
            _successfullyCompletedJobsCount, _completedJobsCount, timer.ElapsedMilliseconds);
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < GetWorkerCount(); i++)
        {
            Task.Run(() => RunWorkerAsync(cancellationToken), cancellationToken);
        }
    }

    private class JobEnvelope
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public required IJobModel Job { get; init; }
        public bool? Result { get; set; }
        public bool IsCompleted => Result is not null;
    }

    public class ConfigurationModel
    {
        public required int WorkerThreadCount { get; init; }
    }
}