using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

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
    IOptions<ThreadConfigurationModel> threadOptions)
    : IHandler
{
    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>
        {
            // Loader thread
            Task.Run(() => jobLoader.RunAsync(cancellationToken), cancellationToken)
        };

        // Executor threads
        for (var i = 0; i < threadOptions.Value.EffectiveWorkerThreadCount; i++)
        {
            var i1 = i;
            tasks.Add(Task.Run(() => jobExecutor.RunAsync(i1, cancellationToken), cancellationToken));
        }

        /*
         * Note: The Maintainer and Idempotency Monitor tasks are intended to abort immediately if configuration or choice of job source doesn't require them.
         * It made for simpler execution in Handler to just run them and add them to the list.
         */

        // Maintainer thread
        tasks.Add(Task.Run(() => maintainer.RunAsync(cancellationToken), cancellationToken));

        // Idempotency monitor thread
        tasks.Add(Task.Run(() => idempotencyMonitor.RunAsync(cancellationToken), cancellationToken));

        // Wait for all to finish (all implementations of worker interfaces should be referencing IExecutionEndArbiter or ILoaderExecutionEndArbiter)
        foreach (var task in tasks)
        {
            await task;
        }
    }
}