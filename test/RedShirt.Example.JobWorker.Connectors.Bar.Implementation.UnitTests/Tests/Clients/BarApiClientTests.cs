using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Clients;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.UnitTests.Tests.Helpers;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.UnitTests.Tests.Clients;

public class BarApiClientTests
{
    private static BarApiClient CreateClient(StubHttpMessageHandler handler)
    {
        return new BarApiClient(new HttpClient(handler), "https://bar.local");
    }

    [Fact]
    public async Task CreateBarAsync_WhenBodyIsNull_ThrowsJsonException()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<JsonException>(() =>
            client.CreateBarAsync(new CreateBarConnectorRequest {Name = "Created"},
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateBarAsync_WhenSuccess_ReturnsMappedResponse()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://bar.local/api/bar", request.RequestUri?.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Id\":99,\"Name\":\"Created\"}", Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);

        var response = await client.CreateBarAsync(new CreateBarConnectorRequest {Name = "Created"},
            TestContext.Current.CancellationToken);

        Assert.Equal(99, response.Id);
        Assert.Equal("Created", response.Name);
    }

    [Fact]
    public async Task GetBarByIdAsync_WhenNotFound_ThrowsBarRecordNotFoundException()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var thrown = await Assert.ThrowsAsync<BarRecordNotFoundException>(() =>
            client.GetBarByIdAsync(404, TestContext.Current.CancellationToken));

        Assert.Equal(404, thrown.Id);
    }

    [Fact]
    public async Task GetBarByIdAsync_WhenRateLimited_ThrowsBarRateLimitedExceptionWithRetryAfter()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "3");
        var handler = new StubHttpMessageHandler(_ => response);
        var client = CreateClient(handler);

        var thrown = await Assert.ThrowsAsync<BarRateLimitedException>(() =>
            client.GetBarByIdAsync(429, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(3), thrown.RetryAfter);
    }

    [Fact]
    public async Task GetBarByIdAsync_WhenSuccess_ReturnsMappedResponse()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"Id\":12,\"Name\":\"Bar-12\"}", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var response = await client.GetBarByIdAsync(12, TestContext.Current.CancellationToken);

        Assert.Equal(12, response.Id);
        Assert.Equal("Bar-12", response.Name);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://bar.local/api/bar/12", handler.Requests[0].RequestUri?.ToString());
    }
}