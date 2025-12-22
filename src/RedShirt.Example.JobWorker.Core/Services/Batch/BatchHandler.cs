using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.Batch;

public interface IHandler
{
    Task HandleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Handle any initialization, then proceed to main worker loop.
/// </summary>
/// <param name="jobManager"></param>
/// <param name="batchWorkerLoop"></param>
internal class BatchHandler(IJobManager jobManager, IBatchWorkerLoop batchWorkerLoop) : IHandler
{
    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        // Kick off the job manager
        await jobManager.StartAsync(cancellationToken);
        // Enter worker loop
        await batchWorkerLoop.RunAsync(cancellationToken);
    }
}