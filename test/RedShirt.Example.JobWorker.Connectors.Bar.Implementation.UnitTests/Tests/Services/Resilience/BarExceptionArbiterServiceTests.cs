using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.UnitTests.Tests.Services.Resilience;

public class BarExceptionArbiterServiceTests
{
    private readonly BarExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_BarRateLimitedException_IsExpectedButNotTransientForInnerRetry()
    {
        var report = _sut.GetReport(new BarRateLimitedException(TimeSpan.FromSeconds(1)));

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        Assert.False(report.AlreadyHandled);
    }

    [Fact]
    public void GetReport_BarRecordNotFoundException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new BarRecordNotFoundException(404));

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_BarUnauthorizedException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new BarUnauthorizedException());

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_HandledBarException_RespectsFlags()
    {
        var report = _sut.GetReport(new BarException(new InvalidOperationException("handled"))
        {
            IsHandled = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = false
        });

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void GetReport_HttpRequestException_WithClientError_IsNotTransient(HttpStatusCode statusCode)
    {
        var report = _sut.GetReport(new HttpRequestException("client error", null, statusCode));

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void GetReport_HttpRequestException_WithTransientStatus_IsTransient(HttpStatusCode statusCode)
    {
        var report = _sut.GetReport(new HttpRequestException("transient", null, statusCode));

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_JsonException_IsNotTransient()
    {
        var report = _sut.GetReport(new JsonException("invalid json"));

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_OAuthRequestExceptionServerError_IsTransient()
    {
        var report = _sut.GetReport(new OAuthRequestException("server error")
        {
            StatusCode = HttpStatusCode.InternalServerError,
            CredentialStorageProblem = false,
            FreshCredentialCacheResult = true
        });

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_OAuthRequestExceptionUnauthorized_IsNotTransient()
    {
        var report = _sut.GetReport(new OAuthRequestException("unauthorized")
        {
            StatusCode = HttpStatusCode.Unauthorized,
            CredentialStorageProblem = false,
            FreshCredentialCacheResult = false
        });

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException());

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_UnwrapsInner()
    {
        var inner = new BarRecordNotFoundException(7);
        var aggregate = new AggregateException(inner);

        var report = _sut.GetReport(aggregate);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SocketException_IsTransient()
    {
        var report = _sut.GetReport(new SocketException());

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnhandledBarExceptionWithTransientFlag_IsTransient()
    {
        var report = _sut.GetReport(new BarException(new InvalidOperationException("unhandled"))
        {
            IsHandled = false,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        });

        Assert.True(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnknownException_IsNotExpected()
    {
        var report = _sut.GetReport(new InvalidOperationException("unexpected"));

        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerSecretManagerException_UsesHandledFlags()
    {
        var report = _sut.GetReport(new WorkerSecretManagerException("secret failure")
        {
            IsHandled = false,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        });

        Assert.True(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }
}