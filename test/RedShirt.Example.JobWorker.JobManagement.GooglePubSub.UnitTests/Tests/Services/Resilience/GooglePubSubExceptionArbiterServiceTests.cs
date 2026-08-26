using Grpc.Core;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;

public class GooglePubSubExceptionArbiterServiceTests
{
    private readonly GooglePubSubExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
#pragma warning disable S3928
        // ReSharper disable once NotResolvedInText
        var report = _sut.GetReport(new ArgumentException("bad subscription", "subscriptionId"));
#pragma warning restore S3928

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_HttpRequestException_IsExpectedAndTransient()
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
            new RpcException(new Status(StatusCode.Unavailable, "a")),
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

    [Theory]
    [InlineData(StatusCode.InvalidArgument, false)]
    [InlineData(StatusCode.NotFound, true)]
    [InlineData(StatusCode.AlreadyExists, false)]
    [InlineData(StatusCode.PermissionDenied, true)]
    [InlineData(StatusCode.Unauthenticated, true)]
    [InlineData(StatusCode.FailedPrecondition, false)]
    [InlineData(StatusCode.OutOfRange, false)]
    [InlineData(StatusCode.Unimplemented, false)]
    [InlineData(StatusCode.DataLoss, false)]
    public void GetReport_RpcException_PermanentCodes_IsExpectedAndNotTransient(
        StatusCode statusCode,
        bool couldBeExternallySolvable)
    {
        var report = _sut.GetReport(new RpcException(new Status(statusCode, "permanent")));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.Unknown)]
    public void GetReport_RpcException_TransientCodes_IsExpectedAndTransient(StatusCode statusCode)
    {
        var report = _sut.GetReport(new RpcException(new Status(statusCode, "transient")));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RpcException_Unavailable_DnsWithoutSubchannelDetail_IsStillTransient()
    {
        var exception = new RpcException(new Status(
            StatusCode.Unavailable,
            "service temporarily unavailable",
            new SocketException((int) SocketError.HostNotFound)));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RpcException_Unavailable_NullDetail_IsStillTransient()
    {
        // Exercises IsSubchannelConnectionDetail(null) via Status.Detail.
        var exception = new RpcException(new Status(StatusCode.Unavailable, null!));

        Assert.Null(exception.Status.Detail);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RpcException_Unavailable_SubchannelConnectionRefused_IsStillTransient()
    {
        var exception = new RpcException(new Status(
            StatusCode.Unavailable,
            "Error connecting to subchannel.",
            new SocketException((int) SocketError.ConnectionRefused)));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.NoData)]
    [InlineData(SocketError.NoRecovery)]
    [InlineData(SocketError.TryAgain)]
    public void GetReport_RpcException_Unavailable_SubchannelDnsFailure_IsNotTransient(SocketError socketError)
    {
        var exception = new RpcException(new Status(
            StatusCode.Unavailable,
            "Error connecting to subchannel.",
            new SocketException((int) socketError)));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RpcException_Unavailable_SubchannelDnsFailure_NestedUnderHttpRequest_IsNotTransient()
    {
        var exception = new RpcException(new Status(
            StatusCode.Unavailable,
            "Error connecting to sub-channel.",
            new HttpRequestException("failed to connect",
                new SocketException((int) SocketError.HostNotFound))));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RpcException_Unavailable_SubchannelWithoutSocketException_IsStillTransient()
    {
        // Subchannel detail matches, but FindSocketException returns null (no Inner/Debug exception).
        var exception = new RpcException(new Status(
            StatusCode.Unavailable,
            "Error connecting to subchannel."));

        Assert.Null(exception.InnerException);
        Assert.Null(exception.Status.DebugException);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RpcException(new Status(StatusCode.Unavailable, "down"));
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