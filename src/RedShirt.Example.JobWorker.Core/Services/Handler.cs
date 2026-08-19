using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Heartbeats;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.Core.Utility;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Kick-off point for message broker job management.
///     It is responsible for starting all worker threads.
/// </summary>
public interface IHandler
{
    Task HandleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Indicates a worker thread meant to be invoked and tracked by the Handler class.
/// </summary>
internal interface IHandlerSubComponent
{
    Task<HandlerComponentResponse> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Kick-off point for message broker job management.
///     It is responsible for starting all worker threads.
/// </summary>
/// <param name="jobLoaderLoop"></param>
/// <param name="heartbeatMaintainer"></param>
/// <param name="jobExecutor"></param>
/// <param name="idempotencyMonitor"></param>
/// <param name="threadOptions"></param>
internal sealed class Handler(
    IJobLoaderLoop jobLoaderLoop,
    IHeartbeatMaintainer heartbeatMaintainer,
    IJobExecutor jobExecutor,
    IIdempotencyMonitor idempotencyMonitor,
    IMessageSubscribeSourceStarter messageSubscribeSourceStarter,
    IOptions<ThreadConfigurationModel> threadOptions,
    ILogger<Handler> logger)
    : IHandler
{
    private readonly SemaphoreSlim _exceptionLock = new(1, 1);
    private readonly AsyncManualResetEvent _workerDoneEvent = new();
    private Exception? _workerException;

    private async Task RunWorkerAsync(Func<Task<HandlerComponentResponse>> callback,
        CancellationToken cancellationToken)
    {
        var handlerResponse = HandlerComponentResponse.Finished;

        try
        {
            handlerResponse = await callback();
        }
        catch (Exception e)
        {
            // Filter out exception tracking where OperationCanceledException is thrown with a cancelled cancellationToken
            // These are assumed to be intentional ctrl-c's in a local environment
            if (e is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(e, "Unhandled exception: {EMessage}", e.Message);
                await _exceptionLock.WaitAsync(cancellationToken);
                try
                {
                    _workerException = e;
                }
                finally
                {
                    _exceptionLock.Release();
                }
            }
        }
        finally
        {
            if (handlerResponse == HandlerComponentResponse.Finished)
            {
                // A noteworthy handler has finished
                _workerDoneEvent.Set();
            }
        }
    }

    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        var tasksLock = new SemaphoreSlim(1, 1);
        var tasks = new List<Task>();

        var addToTaskFunc = new Func<Func<Task<HandlerComponentResponse>>, Task>(async callback =>
        {
            await tasksLock.WaitAsync(cancellationToken);
            try
            {
                tasks.Add(Task.Run(() => RunWorkerAsync(callback, cancellationToken), cancellationToken));
            }
            finally
            {
                tasksLock.Release();
            }
        });

        // Loader loop
        await addToTaskFunc(() => jobLoaderLoop.RunAsync(cancellationToken));
        // Subscription
        await addToTaskFunc(() => messageSubscribeSourceStarter.RunAsync(cancellationToken));

        // Executor threads
        for (var i = 0; i < threadOptions.Value.EffectiveWorkerThreadCount; i++)
        {
            var i1 = i;
            await addToTaskFunc(() => jobExecutor.RunAsync(i1, cancellationToken));
        }

        /*
         * Note: The Maintainer and Idempotency Monitor tasks are intended to abort immediately if configuration or choice of job source doesn't require them.
         * It made for simpler execution in Handler to just run them and add them to the list.
         */

        // Maintainer thread
        await addToTaskFunc(() => heartbeatMaintainer.RunAsync(cancellationToken));

        // Idempotency monitor thread
        await addToTaskFunc(() => idempotencyMonitor.RunAsync(cancellationToken));

        /*
         * Wait for the worker done event to be triggered, indicating the first component to finish.
         * Once it has triggered, one of two things is expected to happen:
         *  * The finished worker was on account of an exception, which shall soon thrown upwards with the intent of crashing the entire program.
         */
        await _workerDoneEvent.WaitAsync(cancellationToken);

        await _exceptionLock.WaitAsync(cancellationToken);
        try
        {
            if (_workerException is not null)
            {
                /*
                 * Now that we've drawn another thread's unexpected Exception into our original thread,
                 * throw it upwards to bring down the whole program.
                 */

                throw _workerException;
            }
        }
        finally
        {
            _exceptionLock.Release();
        }

        // Wait for all the other tasks to finish
        foreach (var task in tasks)
        {
            await task;
        }
    }
}