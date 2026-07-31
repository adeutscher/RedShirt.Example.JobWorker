using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;
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
    Task<HandlerResponseEnum> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Kick-off point for message broker job management.
///     It is responsible for starting all worker threads.
/// </summary>
/// <param name="jobLoader"></param>
/// <param name="maintainer"></param>
/// <param name="jobExecutor"></param>
/// <param name="idempotencyMonitor"></param>
/// <param name="threadOptions"></param>
internal class Handler(
    IJobLoader jobLoader,
    IMaintainer maintainer,
    IJobExecutor jobExecutor,
    IIdempotencyMonitor idempotencyMonitor,
    IOptions<ThreadConfigurationModel> threadOptions,
    ILogger<Handler> logger)
    : IHandler
{
    private readonly SemaphoreSlim _exceptionLock = new(1, 1);
    private readonly AsyncManualResetEvent _workerDoneEvent = new();
    private Exception? _workerException;

    private async Task RunWorkerAsync(Func<Task<HandlerResponseEnum>> callback)
    {
        var handlerResponse = HandlerResponseEnum.Finished;

        try
        {
            handlerResponse = await callback();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception: {EMessage}", e.Message);
            await _exceptionLock.WaitAsync();
            try
            {
                _workerException = e;
            }
            finally
            {
                _exceptionLock.Release();
            }
        }
        finally
        {
            if (handlerResponse == HandlerResponseEnum.Finished)
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

        var addToTaskFunc = new Func<Func<Task<HandlerResponseEnum>>, Task>(async callback =>
        {
            await tasksLock.WaitAsync(cancellationToken);
            try
            {
                tasks.Add(Task.Run(() => RunWorkerAsync(callback), cancellationToken));
            }
            finally
            {
                tasksLock.Release();
            }
        });

        await addToTaskFunc(() => jobLoader.RunAsync(cancellationToken));

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
        await addToTaskFunc(() => maintainer.RunAsync(cancellationToken));

        // Idempotency monitor thread
        await addToTaskFunc(() => idempotencyMonitor.RunAsync(cancellationToken));

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