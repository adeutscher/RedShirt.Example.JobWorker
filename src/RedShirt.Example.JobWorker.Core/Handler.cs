using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core;

public interface IHandler
{
    Task HandleAsync(CancellationToken cancellationToken = default);
}

internal class Handler(IJobSourceBootstrapper jobSourceBootstrapper, IWorkerLoop workerLoop) : IHandler
{
    public Task HandleAsync(CancellationToken cancellationToken = default)
    {
        jobSourceBootstrapper.Bootstrap();
        return workerLoop.RunAsync(cancellationToken);
    }
}