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
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
#pragma warning disable S3928
        // ReSharper disable once NotResolvedInText
        var report = _sut.GetReport(new ArgumentException("bad topic", "topic"));
#pragma warning restore S3928

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_HttpRequestException_WithDnsSocketError_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new HttpRequestException("failed to connect",
            new SocketException((int) SocketError.HostNotFound)));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public void GetReport_HttpRequestException_WithPermanentStatus_IsExpectedAndNotTransient(
        HttpStatusCode statusCode)
    {
        var report = _sut.GetReport(new HttpRequestException("http failed", null, statusCode));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void GetReport_HttpRequestException_WithTransientStatus_IsExpectedAndTransient(
        HttpStatusCode statusCode)
    {
        var report = _sut.GetReport(new HttpRequestException("http failed", null, statusCode));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_HttpRequestException_WithoutStatus_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new HttpRequestException("connection reset"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotExpected()
    {
        var exception = new AggregateException(
            new ConnectException("connect"),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException("caller cancelled"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_PermanentPulsarExceptions_IsExpectedAndNotTransient()
    {
        (Exception Exception, bool CouldBeExternallySolvable)[] exceptions =
        [
            (new AuthenticationException("critical"), true),
            (new AuthorizationException("critical"), true),
            (new InvalidConfigurationException("critical"), false),
            (new UnsupportedVersionException("critical"), false),
            (new InvalidTopicNameException("critical"), false),
            (new TopicDoesNotExistException("missing"), true),
            (new AlreadyClosedException("closed"), false),
            (new TopicTerminatedException("terminated"), true)
        ];

        foreach (var (exception, couldBeExternallySolvable) in exceptions)
        {
            var report = _sut.GetReport(exception);

            Assert.False(report.AlreadyHandled);
            Assert.True(report.IsExpected);
            Assert.False(report.CouldBeTransient);
            Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
        }
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new ConnectException("connect");
        var exception = new AggregateException(inner);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SocketException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new SocketException((int) SocketError.TimedOut));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SystemTimeoutException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TransientPulsarExceptions_IsExpectedAndTransient()
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
            Assert.True(report.IsExpected);
            Assert.True(report.CouldBeTransient);
            Assert.True(report.CouldBeExternallySolvable);
        }
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsNotExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new InvalidOperationException("unexpected failure"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_DoesNotRetry()
    {
        var exception = new WorkerJobSourceException("already handled")
        {
            IsHandled = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = false
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_UnhandledTransient_MayRetry()
    {
        var exception = new WorkerJobSourceException("transient")
        {
            IsHandled = false,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }
}