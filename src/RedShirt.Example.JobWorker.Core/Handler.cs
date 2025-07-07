using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core;

public interface IHandler
{
    Task HandleAsync(CancellationToken cancellationToken = default);
}

internal class Handler(IJobManager jobManager, IWorkerLoop workerLoop) : IHandler
{
    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        await jobManager.StartAsync(cancellationToken);
        await workerLoop.RunAsync(cancellationToken);
    }
}