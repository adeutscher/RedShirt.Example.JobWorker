using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisDistributedExceptionArbiterServiceTests
{
    private readonly RedisDistributedExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad lock name", "lockName"));

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
    public void GetReport_MultiInnerAggregateException_IsCritical()
    {
        var exception = new AggregateException(
            new RedisTimeoutException("timeout", CommandStatus.Unknown),
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
    public void GetReport_RedisServerException_IsNotCriticalAndTransient()
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
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RedisTimeoutException("timeout", CommandStatus.Unknown);
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
        var report = _sut.GetReport(new InvalidOperationException("unexpected"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void GetReport_WorkerDistributedException_IsAlreadyHandledWithFlags(
        bool isCritical,
        bool isTransient)
    {
        var exception = new WorkerDistributedException("wrapped", isCritical, isTransient);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.Equal(isCritical, report.IsCritical);
        Assert.Equal(isTransient, report.CouldBeTransient);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void GetReport_WorkerSecretManagerException_IsAlreadyHandledWithFlags(
        bool isCritical,
        bool isTransient)
    {
        var exception = new WorkerSecretManagerException("secret failure", isCritical, isTransient);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.Equal(isCritical, report.IsCritical);
        // Unhandled WorkerSecretManagerException may still be retried upstream when transient.
        Assert.Equal(isTransient, report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerSecretManagerException_WhenAlreadyHandled_IsNotTransientForUpstream()
    {
        var exception = new WorkerSecretManagerException("secret failure", false, true, true);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }
}