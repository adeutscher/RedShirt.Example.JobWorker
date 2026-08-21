using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Heartbeats;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Kick-off point for message broker job management.
///     It is responsible for starting all worker threads.
/// </summary>
public interface IHandler
{
    /// <summary>
    ///     Handle the operations of running a message consumer.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns><c>true</c> if the handler ended amicably, else return <c>false</c> suggesting an error.</returns>
    Task<bool> HandleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Kick-off point for message broker job management.
///     It is responsible for starting all worker threads.
/// </summary>
/// <param name="executionEndArbiter"></param>
/// <param name="jobLoaderLoop"></param>
/// <param name="heartbeatMaintainer"></param>
/// <param name="jobExecutor"></param>
/// <param name="idempotencyMonitor"></param>
/// <param name="threadOptions"></param>
#pragma warning disable S107
internal sealed class Handler(
    IExecutionEndArbiter executionEndArbiter,
    IJobLoaderLoop jobLoaderLoop,
    IHeartbeatMaintainer heartbeatMaintainer,
    IJobExecutor jobExecutor,
    IIdempotencyMonitor idempotencyMonitor,
    IJobSubscriberManager jobSubscriberManager,
    IOptions<ThreadConfigurationModel> threadOptions,
    ILogger<Handler> logger)
#pragma warning restore S107
    : IHandler
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private bool _hasBeenInvoked;
    private Exception? _workerException;

    private async Task RunWorkerAsync(WorkerThreadType type, Func<Task<HandlerComponentResponse>> callback,
        CancellationToken cancellationToken)
    {
        // Assume an exception until proven otherwise by happy-path execution.
        var handlerResponse = HandlerComponentResponse.Exception;

        try
        {
            handlerResponse = await callback();
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException)
            {
                handlerResponse = HandlerComponentResponse.Cancelled;
            }

            // Filter out exception tracking where OperationCanceledException is thrown with a cancelled cancellationToken
            // These are assumed to be intentional ctrl-c's in a local environment
            if (e is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                executionEndArbiter.Stop(e);

                await _lock.WaitAsync(cancellationToken);
                try
                {
                    _workerException ??= e;
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        logger.LogTrace("Worker thread for {Type} done. Response: {HandlerResponse}", type, handlerResponse);

        if (handlerResponse == HandlerComponentResponse.Finished)
        {
            executionEndArbiter.Stop();
        }
    }

    private void OnStop(Exception? e)
    {
        /*
         * Deliberately not using the blocking synchronous call to wait on the _lock semaphore.
         * A perfectly-timed second exception could *technically* overwrite the first here,
         * but that's so remote that I'm not concerned about it.
         */

        if (e is not null)
        {
            Interlocked.Exchange(ref _workerException, e);
        }
    }

    public async Task<bool> HandleAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_hasBeenInvoked)
            {
                throw new InvalidOperationException("Handler should only be run once.");
            }

            _hasBeenInvoked = true;
        }
        finally
        {
            _lock.Release();
        }

        // The reason for the _hasBeenInvoked check is to enforce that we aren't adding a million callbacks to the execution end arbiter.
        executionEndArbiter.AddOnStopCallback(OnStop);

        var tasksLock = new SemaphoreSlim(1, 1);
        var tasks = new List<Task>();

        var addToTaskFuncAsync =
            new Func<WorkerThreadType, Func<Task<HandlerComponentResponse>>, Task>(async (type, callback) =>
            {
                await tasksLock.WaitAsync(cancellationToken);
                try
                {
                    tasks.Add(Task.Run(() => RunWorkerAsync(type, callback, cancellationToken), cancellationToken));
                }
                finally
                {
                    tasksLock.Release();
                }
            });

        // Loader loop
        await addToTaskFuncAsync(WorkerThreadType.MessagePoller, () => jobLoaderLoop.RunAsync(cancellationToken));
        // Subscription threads
        await addToTaskFuncAsync(WorkerThreadType.JobSubscriberManager,
            () => jobSubscriberManager.RunAsync(cancellationToken));

        // Executor threads
        for (var i = 0; i < threadOptions.Value.EffectiveWorkerThreadCount; i++)
        {
            var i1 = i;
            await addToTaskFuncAsync(WorkerThreadType.JobExecutor, () => jobExecutor.RunAsync(i1, cancellationToken));
        }

        /*
         * Note: The Maintainer and Idempotency Monitor tasks are intended to abort immediately if configuration or choice of job source doesn't require them.
         * It made for simpler execution in Handler to just run them and add them to the list.
         */

        // Maintainer thread
        await addToTaskFuncAsync(WorkerThreadType.HeartbeatMaintainer,
            () => heartbeatMaintainer.RunAsync(cancellationToken));

        // Idempotency monitor thread
        await addToTaskFuncAsync(WorkerThreadType.IdempotencyMonitor,
            () => idempotencyMonitor.RunAsync(cancellationToken));

        /*
         * Wait for the execution end arbiter to stop, indicating the first enabled component to finish or fault.
         * Once it has triggered, the following is expected to happen:
         *  * If the Stop was on account of an exception, then the exception shall be logged
         *  * Waiting for other tasks to cleanly finish.
         */
        await executionEndArbiter.WaitForFinishedAsync(cancellationToken);

        var cleanExecution = true;
        if (Volatile.Read(ref _workerException) is { } workerException)
        {
            /*
             * Now that we've drawn another thread's unexpected Exception into our original thread,
             * log it before exiting the program.
             */
            logger.LogError(workerException, "Unhandled exception: {EMessage}", workerException.Message);
            cleanExecution = false;
        }

        // Wait for all the other tasks to finish
        foreach (var task in tasks)
        {
            await task;
        }

        return cleanExecution;
    }

    private enum WorkerThreadType
    {
        JobExecutor,
        HeartbeatMaintainer,
        IdempotencyMonitor,
        JobSubscriberManager,
        MessagePoller
    }
}