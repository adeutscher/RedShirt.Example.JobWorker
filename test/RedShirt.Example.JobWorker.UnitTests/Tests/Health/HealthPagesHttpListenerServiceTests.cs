using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options; 
using RedShirt.Example.JobWorker.Common.Health.Configuration;
using RedShirt.Example.JobWorker.Common.Health.Constants;
using RedShirt.Example.JobWorker.Common.Health.Models;
using RedShirt.Example.JobWorker.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Services;

namespace RedShirt.Example.JobWorker.UnitTests.Tests.Health;

public class HealthPagesHttpListenerServiceTests
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(1)
    };

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotBindPort()
    {
        var port = GetFreePort();
        var service = CreateService(enabled: false, port: port);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                Client.GetAsync($"http://127.0.0.1:{port}{HealthPathConstants.LivePath}",
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(HealthPathConstants.LivePath)]
    [InlineData(HealthPathConstants.HealthPath)]
    public async Task GetEndpoint_WhenEnabled_ReturnsOk(string path)
    {
        var port = GetFreePort();
        var service = CreateService(enabled: true, port: port);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}{path}");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}{path}",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("OK", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HealthPath_WhenUnhealthy_ReturnsServiceUnavailable()
    {
        var port = GetFreePort();
        var health = new Mock<ICoreHealthStateReaderService>(MockBehavior.Strict);
        health.Setup(h => h.IsHealthy()).Returns(false);
        var service = CreateService(enabled: true, port: port, healthService: health.Object);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}{HealthPathConstants.LivePath}");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}{HealthPathConstants.HealthPath}",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            health.Verify(h => h.IsHealthy(), Times.AtLeastOnce);
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
        var service = CreateService(enabled: true, port: port);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForEndpointAsync($"http://127.0.0.1:{port}{HealthPathConstants.LivePath}");

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/unknown",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static HealthPagesHttpListenerService CreateService(
        bool enabled,
        int port,
        ICoreHealthStateReaderService? healthService = null,
        ICoreStatisticsService? statisticsService = null)
    {
        healthService ??= CreateHealthyReader();
        statisticsService ??= CreateEmptyStatisticsService();

        return new HealthPagesHttpListenerService(
            healthService,
            statisticsService,
            Options.Create(new CommonHealthConfigurationModel { Enabled = enabled }),
            Options.Create(new HealthConfigurationModel { Port = port }),
            NullLogger<HealthPagesHttpListenerService>.Instance);
    }

    private static ICoreHealthStateReaderService CreateHealthyReader()
    {
        var health = new Mock<ICoreHealthStateReaderService>(MockBehavior.Strict);
        health.Setup(h => h.IsHealthy()).Returns(true);
        return health.Object;
    }

    private static ICoreStatisticsService CreateEmptyStatisticsService()
    {
        var statistics = new Mock<ICoreStatisticsService>(MockBehavior.Strict);
        statistics.Setup(s => s.GetStatistics()).Returns(new StatisticsModel
        {
            Uptime = TimeSpan.Zero,
            Lifetime = new JobStatisticsModel
            {
                SuccessfulTimings = new SuccessfulTimingsModel
                {
                    Average = TimeSpan.Zero,
                    Min = TimeSpan.Zero,
                    Max = TimeSpan.Zero
                },
                Totals = new LifetimeTotalsModel
                {
                    Received = 0,
                    Successful = 0,
                    Cancelled = 0,
                    Failed = 0,
                    InvalidData = 0
                }
            }
        });
        return statistics.Object;
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
                using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        throw new TimeoutException($"Endpoint {url} did not become available.");
    }
}
