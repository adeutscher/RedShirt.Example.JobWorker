using Pulsar.Client.Api;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;
using System.Net;
using System.Net.Sockets;
using TimeoutException = System.TimeoutException;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

public class PulsarExceptionArbiterServiceTests
{
    private readonly PulsarExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad topic", "topic"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_CriticalPulsarExceptions_IsCriticalAndNotTransient()
    {
        Exception[] exceptions =
        [
            new AuthenticationException("critical"),
            new AuthorizationException("critical"),
            new InvalidConfigurationException("critical"),
            new UnsupportedVersionException("critical"),
            new InvalidTopicNameException("critical"),
            new AlreadyClosedException("closed"),
            new TopicTerminatedException("terminated")
        ];

        foreach (var exception in exceptions)
        {
            var report = _sut.GetReport(exception);

            Assert.False(report.AlreadyHandled);
            Assert.True(report.IsCritical);
            Assert.False(report.CouldBeTransient);
        }
    }

    [Fact]
    public void GetReport_HttpRequestException_WithDnsSocketError_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new HttpRequestException("failed to connect",
            new SocketException((int) SocketError.HostNotFound)));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public void GetReport_HttpRequestException_WithPermanentStatus_IsNotCriticalAndNotTransient(
        HttpStatusCode statusCode)
    {
        var report = _sut.GetReport(new HttpRequestException("http failed", null, statusCode));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void GetReport_HttpRequestException_WithTransientStatus_IsNotCriticalAndTransient(
        HttpStatusCode statusCode)
    {
        var report = _sut.GetReport(new HttpRequestException("http failed", null, statusCode));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_HttpRequestException_WithoutStatus_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new HttpRequestException("connection reset"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsCritical()
    {
        var exception = new AggregateException(
            new ConnectException("connect"),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException("caller cancelled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new ConnectException("connect");
        var exception = new AggregateException(inner);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SocketException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new SocketException((int) SocketError.TimedOut));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SystemTimeoutException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TransientPulsarExceptions_IsNotCriticalAndTransient()
    {
        Exception[] exceptions =
        [
            new ConnectException("connect"),
            new LookupException("lookup"),
            new TooManyRequestsException("busy"),
            new ConsumerBusyException("busy"),
            new RequestTimeoutException("timeout")
        ];

        foreach (var exception in exceptions)
        {
            var report = _sut.GetReport(exception);

            Assert.False(report.AlreadyHandled);
            Assert.False(report.IsCritical);
            Assert.True(report.CouldBeTransient);
        }
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new InvalidOperationException("unexpected failure"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_DoesNotRetry()
    {
        var exception = new WorkerJobSourceException("already handled", false, true,
            true);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_UnhandledTransient_MayRetry()
    {
        var exception = new WorkerJobSourceException("transient", false, true);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }
}