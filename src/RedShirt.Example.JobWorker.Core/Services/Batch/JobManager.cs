using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.Batch;

/// <summary>
///     Handles multithreading and heartbeats for jobs.
/// </summary>
/// <param name="executionEndArbiter"></param>
/// <param name="safeJobRunner"></param>
/// <param name="jobSource"></param>
/// <param name="logger"></param>
/// <param name="options"></param>
internal class JobManager(
    IExecutionEndArbiter executionEndArbiter,
    ISafeJobRunner safeJobRunner,
    IJobSource jobSource,
    ILogger<JobManager> logger,
    IOptions<ThreadConfigurationModel> options) : IJobManager
{
    private readonly SemaphoreSlim _completedJobsCountSemaphore = new(1, 1);
    private readonly SemaphoreSlim _completedWorkersCountSemaphore = new(1, 1);

    private readonly ConcurrentQueue<JobEnvelope> _queue = new();

    private readonly ManualResetEvent _readyToReceiveJobsWaitHandle = new(false);
    private readonly SemaphoreSlim _startSemaphore = new(1, 1);
    private readonly AutoResetEvent _workerCompleteEvent = new(false);

    private readonly List<WaitHandle> _workerWaitHandles = new();
    private int _completedJobsCount;

    private int _completedWorkersCount;

    private bool _isLoadingJobs;
    private int _successfullyCompletedJobsCount;

    private uint _totalBatches;
    private ulong _totalJobs;

    private async Task RunWorkerAsync(CancellationToken cancellationToken = default)
    {
        var waitHandler = new ManualResetEvent(false);

        await _startSemaphore.WaitAsync(cancellationToken);
        try
        {
            _workerWaitHandles.Add(waitHandler);
        }
        finally
        {
            _startSemaphore.Release();
        }

        while (executionEndArbiter.ShouldKeepRunning())
        {
            waitHandler.Set();
            _readyToReceiveJobsWaitHandle.WaitOne();

            while (true)
            {
                var gotJob = _queue.TryDequeue(out var item);
                if (!gotJob && _isLoadingJobs)
                {
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                if (!gotJob)
                {
                    break;
                }

                var result = await safeJobRunner.RunSafelyAsync(item!.Job, cancellationToken);

                var task1 = UpdateJobCountsAsync(result, cancellationToken);
                var task2 = AcknowledgeCompletionAsync(item, result, cancellationToken);
                Task.WaitAll([task1, task2], cancellationToken);
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

    private async Task AcknowledgeCompletionAsync(JobEnvelope item, bool result,
        CancellationToken cancellationToken = default)
    {
        await item.Semaphore.WaitAsync(cancellationToken);
        try
        {
            item.Result = result;
            await jobSource.AcknowledgeCompletionAsync(item.Job, result, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Job acknowledge failed");
            item.Result = false;
        }
        finally
        {
            item.Semaphore.Release();
        }
    }

    private async Task UpdateJobCountsAsync(bool result, CancellationToken cancellationToken)
    {
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
    }

    private async Task HeartbeatMonitorAsync(AutoResetEvent bootstrapEvent, ManualResetEvent resetEvent,
        List<JobEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        while (envelopes.Count > 0)
        {
            bootstrapEvent.Set();
            if (resetEvent.WaitOne(TimeSpan.FromSeconds(jobSource.RecommendedHeartbeatIntervalSeconds)))
            {
                return;
            }

            for (var i = envelopes.Count - 1; i >= 0; i--)
            {
                var item = envelopes[i]; // shorthand

                await item.Semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (item.IsCompleted)
                    {
                        envelopes.RemoveAt(i);
                        continue;
                    }

                    await jobSource.HeartbeatAsync(item.Job, cancellationToken);
                }
                catch (CanNoLongerHeartbeatException e)
                {
                    // Same as a general exception, but with a specific log message
                    logger.LogWarning(e, "Can no longer heartbeat message: {MessageId}", item.Job.MessageId);
                    envelopes.RemoveAt(i);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error while running heartbeat: {EMessage}", e.Message);
                    envelopes.RemoveAt(i);
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

        WaitHandle.WaitAll(_workerWaitHandles.ToArray());

        _readyToReceiveJobsWaitHandle.Set();

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
        _readyToReceiveJobsWaitHandle.Reset();

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
        Task? heartbeatTask = null;
        if (jobSource.RecommendedHeartbeatIntervalSeconds > 0)
        {
            var bootstrapEvent = new AutoResetEvent(false);
            heartbeatTask =
                Task.Run(
                    () => HeartbeatMonitorAsync(bootstrapEvent, heartbeatDoneEvent, envelopes,
                        cancellationToken), cancellationToken);
            bootstrapEvent.WaitOne(TimeSpan.FromSeconds(5));
        }

        // Wait for completion

        while (true)
        {
            _workerCompleteEvent.WaitOne(TimeSpan.FromSeconds(1));
            if (_completedWorkersCount == options.Value.EffectiveWorkerThreadCount)
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

        _totalJobs += (uint) envelopes.Count;
        logger.LogTrace("Total Jobs: {TotalJobs} ({TotalBatches} batches)", _totalJobs, ++_totalBatches);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < options.Value.EffectiveWorkerThreadCount; i++)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => RunWorkerAsync(cancellationToken), cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        // Make sure we aren't being asked to manage jobs before the worker threads are ready 
        while (_workerWaitHandles.Count != options.Value.EffectiveWorkerThreadCount)
        {
            await Task.Delay(1, cancellationToken);
        }
    }

    private sealed class JobEnvelope
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public required IJobModel Job { get; init; }
        public bool? Result { get; set; }
        public bool IsCompleted => Result is not null;
    }
}