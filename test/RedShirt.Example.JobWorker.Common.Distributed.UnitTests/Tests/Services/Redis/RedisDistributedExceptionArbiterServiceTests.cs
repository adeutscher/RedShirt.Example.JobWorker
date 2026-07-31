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
    public void GetReport_GenericRedisException_IsFreshAndTransient()
    {
        var report = _sut.GetReport(new RedisException("unexpected Redis failure"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotTransient()
    {
        var exception = new AggregateException(
            new RedisTimeoutException("timeout", CommandStatus.Unknown),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsFreshAndNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException("caller cancelled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(ConnectionFailureType.UnableToConnect, true)]
    [InlineData(ConnectionFailureType.SocketFailure, true)]
    [InlineData(ConnectionFailureType.SocketClosed, true)]
    [InlineData(ConnectionFailureType.Loading, true)]
    [InlineData(ConnectionFailureType.UnableToResolvePhysicalConnection, true)]
    [InlineData(ConnectionFailureType.AuthenticationFailure, false)]
    [InlineData(ConnectionFailureType.ProtocolFailure, false)]
    public void GetReport_RedisConnectionException_DependsOnFailureType(
        ConnectionFailureType failureType,
        bool expectedTransient)
    {
        var exception = new RedisConnectionException(failureType, "connection issue");

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.Equal(expectedTransient, report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RedisServerException_IsFreshAndTransient()
    {
        var report = _sut.GetReport(new RedisServerException("LOADING Redis is loading the dataset in memory"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RedisTimeoutException_IsFreshAndTransient()
    {
        var report = _sut.GetReport(new RedisTimeoutException("command timed out", CommandStatus.Unknown));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RedisTimeoutException("timeout", CommandStatus.Unknown);
        var exception = new AggregateException(inner);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SocketException_IsFreshAndTransient()
    {
        var report = _sut.GetReport(new SocketException((int) SocketError.TimedOut));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsFreshAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TimeoutException_IsFreshAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsFreshAndNotTransient()
    {
        var report = _sut.GetReport(new InvalidOperationException("unexpected"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetReport_WorkerDistributedException_IsAlreadyHandled(bool isTransient)
    {
        var exception = new WorkerDistributedException("wrapped", isTransient);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.Equal(isTransient, report.CouldBeTransient);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetReport_WorkerSecretManagerException_IsAlreadyHandled(bool isTransient)
    {
        var exception = new WorkerSecretManagerException("secret failure", isTransient);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.Equal(isTransient, report.CouldBeTransient);
    }
}