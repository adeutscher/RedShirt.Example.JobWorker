using RedShirt.Example.JobWorker.Common.Health.Constants;
using System.Net;

namespace RedShirt.Example.JobWorker.HealthCheck.UnitTests;

public class HealthCheckAppTests
{
    [Fact]
    public void BuildUri_UsesHealthPathConstant()
    {
        var uri = HealthCheckApp.BuildUri("http://127.0.0.1", 8081);

        Assert.Equal($"http://127.0.0.1:8081{HealthPathConstants.HealthPath}", uri.ToString());
    }

    [Fact]
    public void ParseArgs_ReadsAllOptions()
    {
        var parsed = HealthCheckApp.ParseArgs([
            "--base-url", "http://localhost/", "--port", "8081", "--health-enabled", "true"
        ]);

        Assert.Equal("http://localhost", parsed.BaseUrl);
        Assert.Equal(8081, parsed.Port);
        Assert.True(parsed.HealthEnabled);
        Assert.False(parsed.ShowHelp);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public void ParseArgs_ReadsHealthEnabled(string value, bool expected)
    {
        var parsed = HealthCheckApp.ParseArgs(["--health-enabled", value]);

        Assert.Equal(expected, parsed.HealthEnabled);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void ParseArgs_ShowHelp(string flag)
    {
        var parsed = HealthCheckApp.ParseArgs([flag]);

        Assert.True(parsed.ShowHelp);
    }

    [Fact]
    public void ParseArgs_WhenMissing_ReturnsNulls()
    {
        var parsed = HealthCheckApp.ParseArgs([]);

        Assert.Null(parsed.BaseUrl);
        Assert.Null(parsed.Port);
        Assert.Null(parsed.HealthEnabled);
        Assert.False(parsed.ShowHelp);
    }

    [Fact]
    public async Task RunAsync_WhenHealthDisabled_ReturnsZeroWithoutHttp()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not be called"));

        var exitCode = await HealthCheckApp.RunAsync(
            ["--health-enabled", "false"],
            handler,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task RunAsync_WhenHealthEnabledMissing_ReturnsOne()
    {
        await using var error = new StringWriter();

        var exitCode = await HealthCheckApp.RunAsync(
            ["--base-url", "http://127.0.0.1", "--port", "8081"],
            error: error,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("--health-enabled", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenHelp_ReturnsZeroAndWritesUsage()
    {
        await using var error = new StringWriter();

        var exitCode = await HealthCheckApp.RunAsync(["--help"], error: error,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains(HealthPathConstants.HealthPath, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenNotOk_ReturnsOne()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var exitCode = await HealthCheckApp.RunAsync(
            ["--health-enabled", "true", "--base-url", "http://127.0.0.1", "--port", "8081"],
            handler,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_WhenOk_ReturnsZero()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var exitCode = await HealthCheckApp.RunAsync(
            ["--health-enabled", "true", "--base-url", "http://127.0.0.1", "--port", "8081"],
            handler,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal($"http://127.0.0.1:8081{HealthPathConstants.HealthPath}", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task RunAsync_WhenRequestThrows_ReturnsOne()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("boom"));

        var exitCode = await HealthCheckApp.RunAsync(
            ["--health-enabled", "true", "--base-url", "http://127.0.0.1", "--port", "8081"],
            handler,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("--health-enabled", "true")]
    [InlineData("--health-enabled", "true", "--base-url", "http://127.0.0.1")]
    [InlineData("--health-enabled", "true", "--port", "8081")]
    public async Task RunAsync_WhenRequiredArgsMissingWhileEnabled_ReturnsOne(params string[] args)
    {
        await using var error = new StringWriter();

        var exitCode = await HealthCheckApp.RunAsync(args, error: error,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("required", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responder(request));
        }
    }
}