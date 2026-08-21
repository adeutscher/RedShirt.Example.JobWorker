using Microsoft.Extensions.Hosting;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Services;

public sealed class JobWorkerHostedService(IHandler handler, IHostApplicationLifetime hostApplicationLifetime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await handler.HandleAsync(stoppingToken))
            {
                Environment.ExitCode = 1;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host shutdown / Ctrl+C. Other cancellations still propagate.
        }

        hostApplicationLifetime.StopApplication();
    }
}