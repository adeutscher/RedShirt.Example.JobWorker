using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Configuration;
using RedShirt.Example.JobWorker.Services;

namespace RedShirt.Example.JobWorker.UnitTests.Tests.Health;

public class HealthZPagesHttpListenerServiceTests
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(1)
    };

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotBindPort()
    {
        var port = GetFreePort();
        var service = CreateService(new HealthConfigurationModel { Enabled = false, Port = port });

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                Client.GetAsync($"http://127.0.0.1:{port}/livez", TestContext.Current.CancellationToken));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("/livez")]
    [InlineData("/healthz")]
    public async Task GetEndpoint_WhenEnabled_ReturnsOk(string path)
    {
        var port = GetFreePort();
        var service = CreateService(new HealthConfigurationModel { Enabled = true, Port = port });

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}{path}");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}{path}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("OK", await response.Content.ReadAsStringAsync());
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
        var service = CreateService(new HealthConfigurationModel { Enabled = true, Port = port });

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

    private static HealthZPagesHttpListenerService CreateService(HealthConfigurationModel configurationModel)
    {
        return new HealthZPagesHttpListenerService(
            Options.Create(configurationModel),
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
