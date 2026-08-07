using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

public class RedisStreamsExceptionArbiterServiceTests
{
    private readonly RedisStreamsExceptionArbiterService _sut = new();

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
    public void GetReport_GenericRedisException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new RedisException("unexpected Redis failure"));

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
    public void GetReport_RedisServerException_Loading_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new RedisServerException(RedisErrorKind.Loading, CommandFlags.None,
            "LOADING Redis is loading the dataset in memory"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_RedisServerException_NoGroup_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new RedisServerException(RedisErrorKind.Unknown, CommandFlags.None,
            "NOGROUP No such key 'jobs' or consumer group 'job-worker'"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
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
    public void GetReport_SingleInnerAggregateException_Unwraps()
    {
        var exception =
            new AggregateException(new RedisTimeoutException(CommandFlags.None, "timeout", CommandStatus.Unknown));

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
        var report = _sut.GetReport(new TaskCanceledException());

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsNotExpected()
    {
        var report = _sut.GetReport(new InvalidOperationException("boom"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, false)]
    public void GetReport_WorkerDistributedException_AlreadyHandled(
        bool isHandled,
        bool couldBeTransient,
        bool expectedTransient,
        bool couldBeExternallySolvable)
    {
        var wrapped = new WorkerDistributedException("Not currently connected")
        {
            IsHandled = isHandled,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };

        var report = _sut.GetReport(wrapped);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.Equal(expectedTransient, report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_AlreadyHandled()
    {
        var wrapped =
            new WorkerJobSourceException(new RedisTimeoutException(CommandFlags.None, "t", CommandStatus.Unknown))
            {
                IsHandled = true,
                CouldBeTransient = true,
                CouldBeExternallySolvable = true
            };

        var report = _sut.GetReport(wrapped);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }
}