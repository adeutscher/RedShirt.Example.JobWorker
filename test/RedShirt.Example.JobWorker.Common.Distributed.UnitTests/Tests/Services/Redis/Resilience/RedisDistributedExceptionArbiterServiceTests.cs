using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis.Resilience;

public class RedisDistributedExceptionArbiterServiceTests
{
    private readonly RedisDistributedExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
#pragma warning disable S3928
        // ReSharper disable once NotResolvedInText
        var report = _sut.GetReport(new ArgumentException("bad lock name", "lockName"));
#pragma warning restore S3928

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_GenericRedisException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new RedisException("unexpected Redis failure"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotExpected()
    {
        var exception = new AggregateException(
            new RedisTimeoutException(CommandFlags.None, "timeout", CommandStatus.Unknown),
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
    [InlineData(ConnectionFailureType.UnableToConnect, true, true)]
    [InlineData(ConnectionFailureType.SocketFailure, true, true)]
    [InlineData(ConnectionFailureType.SocketClosed, true, true)]
    [InlineData(ConnectionFailureType.Loading, true, true)]
    [InlineData(ConnectionFailureType.UnableToResolvePhysicalConnection, true, true)]
    [InlineData(ConnectionFailureType.AuthenticationFailure, false, true)]
    [InlineData(ConnectionFailureType.ProtocolFailure, false, false)]
    [InlineData(ConnectionFailureType.ConnectionDisposed, false, false)]
    [InlineData(ConnectionFailureType.InternalFailure, false, false)]
    public void GetReport_RedisConnectionException_ClassifiesByFailureType(
        ConnectionFailureType failureType,
        bool expectedTransient,
        bool expectedExternallySolvable)
    {
        var exception = new RedisConnectionException(failureType, CommandFlags.None, "connection issue");

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.Equal(expectedTransient, report.CouldBeTransient);
        Assert.Equal(expectedExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RedisServerException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new RedisServerException(RedisErrorKind.Loading, CommandFlags.None,
            "LOADING Redis is loading the dataset in memory"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RedisTimeoutException_IsExpectedAndTransient()
    {
        var report =
            _sut.GetReport(new RedisTimeoutException(CommandFlags.None, "command timed out", CommandStatus.Unknown));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RedisTimeoutException(CommandFlags.None, "timeout", CommandStatus.Unknown);
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
        var report = _sut.GetReport(new InvalidOperationException("unexpected"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, false)]
    public void GetReport_WorkerDistributedException_IsAlreadyHandledWithFlags(
        bool isHandled,
        bool couldBeTransient,
        bool expectedTransient,
        bool couldBeExternallySolvable)
    {
        var exception = new WorkerDistributedException("wrapped")
        {
            IsHandled = isHandled,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.Equal(expectedTransient, report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, false)]
    public void GetReport_WorkerSecretManagerException_IsAlreadyHandledWithFlags(
        bool isHandled,
        bool couldBeTransient,
        bool expectedTransient,
        bool couldBeExternallySolvable)
    {
        var exception = new WorkerSecretManagerException("secret failure")
        {
            IsHandled = isHandled,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        // Unhandled WorkerSecretManagerException may still be retried upstream when transient.
        Assert.Equal(expectedTransient, report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerSecretManagerException_WhenAlreadyHandled_IsNotTransientForUpstream()
    {
        var exception = new WorkerSecretManagerException("secret failure")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = true};

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }
}