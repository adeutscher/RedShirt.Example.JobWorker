using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core;

public interface IHandler
{
    Task HandleAsync(CancellationToken cancellationToken = default);
}

internal class Handler(IJobManager jobManager, IWorkerLoop workerLoop) : IHandler
{
    public Task HandleAsync(CancellationToken cancellationToken = default)
    {
        jobManager.Start(cancellationToken);
        return workerLoop.RunAsync(cancellationToken);
    }
}