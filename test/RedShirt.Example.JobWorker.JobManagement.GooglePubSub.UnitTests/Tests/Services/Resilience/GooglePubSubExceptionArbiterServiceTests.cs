using Grpc.Core;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;

public class GooglePubSubExceptionArbiterServiceTests
{
    private readonly GooglePubSubExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad subscription", "subscriptionId"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_HttpRequestException_IsNotCriticalAndTransient()
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
            new RpcException(new Status(StatusCode.Unavailable, "a")),
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

    [Theory]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.OutOfRange)]
    [InlineData(StatusCode.Unimplemented)]
    [InlineData(StatusCode.DataLoss)]
    public void GetReport_RpcException_PermanentCodes_IsNotCriticalAndNotTransient(StatusCode statusCode)
    {
        var report = _sut.GetReport(new RpcException(new Status(statusCode, "permanent")));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.Unknown)]
    public void GetReport_RpcException_TransientCodes_IsNotCriticalAndTransient(StatusCode statusCode)
    {
        var report = _sut.GetReport(new RpcException(new Status(statusCode, "transient")));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
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
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RpcException_Unavailable_NullDetail_IsStillTransient()
    {
        // Exercises IsSubchannelConnectionDetail(null) via Status.Detail.
        var exception = new RpcException(new Status(StatusCode.Unavailable, null!));

        Assert.Null(exception.Status.Detail);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
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
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
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
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
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
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
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
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RpcException(new Status(StatusCode.Unavailable, "down"));
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
    public void GetReport_TaskCanceledException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TimeoutException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
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