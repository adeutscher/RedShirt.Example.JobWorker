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