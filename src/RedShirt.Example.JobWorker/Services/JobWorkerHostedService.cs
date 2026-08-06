using Microsoft.Extensions.Hosting;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Services;

public sealed class JobWorkerHostedService(IHandler handler) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return handler.HandleAsync(cancellationToken);
    }
}
