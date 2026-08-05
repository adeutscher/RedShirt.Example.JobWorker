using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Health;

namespace RedShirt.Example.JobWorker.UnitTests.Tests.Health;

public class WorkerReadyHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenReady_ReturnsHealthy()
    {
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        readiness.Setup(r => r.IsReady()).Returns(true);

        var check = new WorkerReadyHealthCheck(readiness.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNotReady_ReturnsUnhealthy()
    {
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        readiness.Setup(r => r.IsReady()).Returns(false);

        var check = new WorkerReadyHealthCheck(readiness.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
