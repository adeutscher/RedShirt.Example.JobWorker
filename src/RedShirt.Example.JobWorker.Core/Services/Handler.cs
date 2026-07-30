using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
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
/// <param name="jobSource"></param>
/// <param name="jobLoader"></param>
/// <param name="maintainer"></param>
/// <param name="jobExecutor"></param>
/// <param name="threadOptions"></param>
internal class Handler(
    IJobSource jobSource,
    IJobLoader jobLoader,
    IMaintainer maintainer,
    IJobExecutor jobExecutor,
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

        // Maintainer thread
        if (jobSource.RecommendedHeartbeatIntervalSeconds > 0)
        {
            // No point in starting up the maintainer if the job source implementation does not need heartbeats.
            // If RecommendedHeartbeatIntervalSeconds==0, then long jobs rely on underlying message broker library to keep jobs 'in flight'. 
            tasks.Add(Task.Run(() => maintainer.RunAsync(cancellationToken), cancellationToken));
        }

        // Wait for all to finish (all implementations of worker interfaces should be referencing IExecutionEndArbiter or ILoaderExecutionEndArbiter)
        foreach (var task in tasks)
        {
            await task;
        }
    }
}