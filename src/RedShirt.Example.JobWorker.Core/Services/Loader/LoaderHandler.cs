using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch;

namespace RedShirt.Example.JobWorker.Core.Services.Loader;

/// <summary>
///     Kick-off point for Loader-style job management.
///     It is responsible for starting all worker threads.
/// </summary>
/// <param name="jobSource"></param>
/// <param name="jobLoader"></param>
/// <param name="maintainer"></param>
/// <param name="executor"></param>
/// <param name="threadOptions"></param>
internal class LoaderHandler(
    IJobSource jobSource,
    IJobLoader jobLoader,
    IMaintainer maintainer,
    IExecutor executor,
    IOptions<ThreadConfigurationModel> threadOptions)
    : IHandler
{
    public Task HandleAsync(CancellationToken cancellationToken = default)
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
            tasks.Add(Task.Run(() => executor.RunAsync(i1, cancellationToken), cancellationToken));
        }

        // Maintainer thread
        if (jobSource.RecommendedHeartbeatIntervalSeconds > 0)
        {
            // No point in starting up the maintainer if the job source implementation does not need heartbeats.
            // If RecommendedHeartbeatIntervalSeconds==0, then long jobs rely on underlying message broker library to keep jobs 'in flight'. 
            tasks.Add(Task.Run(() => maintainer.RunAsync(cancellationToken), cancellationToken));
        }

        // Wait for all to finish (all implementations of worker interfaces should be referencing IExecutionEndArbiter or ILoaderExecutionEndArbiter)
        Task.WaitAll(tasks.ToArray(), cancellationToken);

        return Task.CompletedTask;
    }
}