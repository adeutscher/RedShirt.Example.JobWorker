using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

public class NatsExceptionArbiterServiceTests
{
    private readonly NatsExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad stream", "stream"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_IOException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new IOException("connection reset"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotExpected()
    {
        var exception = new AggregateException(
            new NatsTimeoutException(),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(400, false, false)]
    [InlineData(409, false, false)]
    [InlineData(401, false, true)]
    [InlineData(403, false, true)]
    [InlineData(404, false, true)]
    [InlineData(408, true, true)]
    [InlineData(429, true, true)]
    [InlineData(500, true, true)]
    [InlineData(503, true, true)]
    public void GetReport_NatsJSApiException_ClassifiesByStatusCode(
        int code,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        var report = _sut.GetReport(new NatsJSApiException(new ApiError
        {
            Code = code,
            Description = "api error",
            ErrCode = 1
        }));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.Equal(couldBeTransient, report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSApiNoResponseException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsJSApiNoResponseException());

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSConnectionException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsJSConnectionException("disconnected"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSDuplicateMessageException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new NatsJSDuplicateMessageException(42));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new NatsJSException("generic jetstream failure"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSProtocolException_ConsumerDeleted_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new NatsJSProtocolException(404, NatsHeaders.Messages.ConsumerDeleted,
            "consumer deleted"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSProtocolException_RequestTimeout_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsJSProtocolException(503, NatsHeaders.Messages.RequestTimeout, "timeout"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsJSTimeoutException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsJSTimeoutException("fetch"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsNoRespondersException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsNoRespondersException());

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsPayloadTooLargeException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new NatsPayloadTooLargeException("too big"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsServerException_AuthorizationViolation_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new NatsServerException("Authorization Violation"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsServerException_Generic_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsServerException("slow consumer"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NatsTimeoutException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new NatsTimeoutException());

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
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
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new NatsTimeoutException();
        var report = _sut.GetReport(new AggregateException(inner));

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
    public void GetReport_TaskCanceledException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TimeoutException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
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
            CouldBeExternallySolvable = true
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
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