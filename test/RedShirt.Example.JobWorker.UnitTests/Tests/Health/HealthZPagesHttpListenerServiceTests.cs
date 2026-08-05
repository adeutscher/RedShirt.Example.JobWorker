using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Services;

namespace RedShirt.Example.JobWorker.UnitTests.Tests.Health;

public class HealthZPagesHttpListenerServiceTests
{
    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotBindPort()
    {
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        var service = CreateService(new HealthOptions { Enabled = false, Port = GetFreePort() }, readiness.Object);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("/livez")]
    [InlineData("/healthz")]
    public async Task GetEndpoint_WhenEnabled_ReturnsOk(string path)
    {
        var port = GetFreePort();
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        var service = CreateService(new HealthOptions { Enabled = true, Port = port }, readiness.Object);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}{path}");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}{path}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReadyZ_WhenReady_ReturnsOk()
    {
        var port = GetFreePort();
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        readiness.Setup(r => r.IsReady()).Returns(true);

        var service = CreateService(new HealthOptions { Enabled = true, Port = port }, readiness.Object);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}/readyz");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/readyz");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", await response.Content.ReadAsStringAsync());
            readiness.Verify(r => r.IsReady(), Times.AtLeastOnce);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReadyZ_WhenNotReady_ReturnsServiceUnavailable()
    {
        var port = GetFreePort();
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        readiness.Setup(r => r.IsReady()).Returns(false);

        var service = CreateService(new HealthOptions { Enabled = true, Port = port }, readiness.Object);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}/readyz");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/readyz");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("not ready", await response.Content.ReadAsStringAsync());
            readiness.Verify(r => r.IsReady(), Times.AtLeastOnce);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GetEndpoint_WhenUnknownPath_ReturnsNotFound()
    {
        var port = GetFreePort();
        var readiness = new Mock<IWorkerReadiness>(MockBehavior.Strict);
        var service = CreateService(new HealthOptions { Enabled = true, Port = port }, readiness.Object);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}/livez");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/unknown");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static HealthZPagesHttpListenerService CreateService(HealthOptions options, IWorkerReadiness readiness)
    {
        return new HealthZPagesHttpListenerService(
            Options.Create(options),
            readiness,
            NullLogger<HealthZPagesHttpListenerService>.Instance);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForEndpointAsync(string url, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        using var client = new HttpClient();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(50);
            }
        }

        throw new TimeoutException($"Endpoint {url} did not become available.");
    }
}
