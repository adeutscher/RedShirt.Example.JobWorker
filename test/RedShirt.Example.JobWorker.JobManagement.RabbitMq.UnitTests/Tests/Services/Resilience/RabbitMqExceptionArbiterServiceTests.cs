using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services.Resilience;

public class RabbitMqExceptionArbiterServiceTests
{
    private readonly RabbitMqExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_AlreadyClosedException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new AlreadyClosedException(
            new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED")));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad queue", "queueName"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_AuthenticationFailureException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new AuthenticationFailureException("ACCESS_REFUSED"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_BrokerUnreachableException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new BrokerUnreachableException(new IOException("no broker")));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_ChannelAllocationException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new ChannelAllocationException());

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_ConnectFailureException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new ConnectFailureException("connect failed", new IOException("refused")));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
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
            new BrokerUnreachableException(new IOException("a")),
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
    public void GetReport_ObjectDisposedException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new ObjectDisposedException("channel"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
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
    public void GetReport_OperationInterruptedException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new OperationInterruptedException());

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_PacketNotRecognizedException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new PacketNotRecognizedException(1, 2, 3, 4));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_PossibleAuthenticationFailureException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new PossibleAuthenticationFailureException("likely ACCESS_REFUSED"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_ProtocolVersionMismatchException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new ProtocolVersionMismatchException(0, 9, 1, 0));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new BrokerUnreachableException(new IOException("down"));
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
    public void GetReport_WireFormattingException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new WireFormattingException("bad frame"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void GetReport_WorkerSecretManagerException_IsAlreadyHandledWithFlags(
        bool isHandled,
        bool couldBeTransient,
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
        Assert.Equal(couldBeTransient, report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }
}