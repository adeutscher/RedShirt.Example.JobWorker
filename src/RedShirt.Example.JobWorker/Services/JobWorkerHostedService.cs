using Microsoft.Extensions.Hosting;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Services;

public sealed class JobWorkerHostedService(IHandler handler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on host shutdown / Ctrl+C. Other cancellations still propagate.
        }
    }
}