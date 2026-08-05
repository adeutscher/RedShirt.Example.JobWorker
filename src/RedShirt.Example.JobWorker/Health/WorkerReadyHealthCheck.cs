using Microsoft.Extensions.Diagnostics.HealthChecks;
using RedShirt.Example.JobWorker.Core.Services.Health;

namespace RedShirt.Example.JobWorker.Health;

public sealed class WorkerReadyHealthCheck(IWorkerReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            readiness.IsReady()
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy());
    }
}
