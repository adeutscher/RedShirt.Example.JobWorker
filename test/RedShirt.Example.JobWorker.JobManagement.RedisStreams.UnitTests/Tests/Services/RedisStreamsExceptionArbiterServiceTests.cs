using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

public class RedisStreamsExceptionArbiterServiceTests
{
    private readonly RedisStreamsExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad stream", "stream"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_GenericRedisException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new RedisException("unexpected Redis failure"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
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
    [InlineData(ConnectionFailureType.UnableToConnect, false, true)]
    [InlineData(ConnectionFailureType.SocketFailure, false, true)]
    [InlineData(ConnectionFailureType.SocketClosed, false, true)]
    [InlineData(ConnectionFailureType.Loading, false, true)]
    [InlineData(ConnectionFailureType.UnableToResolvePhysicalConnection, false, true)]
    [InlineData(ConnectionFailureType.AuthenticationFailure, true, false)]
    [InlineData(ConnectionFailureType.ProtocolFailure, true, false)]
    [InlineData(ConnectionFailureType.ConnectionDisposed, true, false)]
    [InlineData(ConnectionFailureType.InternalFailure, true, false)]
    public void GetReport_RedisConnectionException_ClassifiesByFailureType(
        ConnectionFailureType failureType,
        bool expectedCritical,
        bool expectedTransient)
    {
        var exception = new RedisConnectionException(failureType, "connection issue");

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.Equal(expectedCritical, report.IsCritical);
        Assert.Equal(expectedTransient, report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RedisServerException_NoGroup_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new RedisServerException(
            "NOGROUP No such key 'jobs' or consumer group 'job-worker'"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RedisServerException_Loading_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new RedisServerException("LOADING Redis is loading the dataset in memory"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RedisTimeoutException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new RedisTimeoutException("command timed out", CommandStatus.Unknown));

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
        var report = _sut.GetReport(new TaskCanceledException());

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsCritical()
    {
        var report = _sut.GetReport(new InvalidOperationException("boom"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_AlreadyHandled()
    {
        var wrapped = new WorkerJobSourceException(new RedisTimeoutException("t", CommandStatus.Unknown), false, true,
            true);

        var report = _sut.GetReport(wrapped);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_Unwraps()
    {
        var exception = new AggregateException(new RedisTimeoutException("timeout", CommandStatus.Unknown));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }
}
