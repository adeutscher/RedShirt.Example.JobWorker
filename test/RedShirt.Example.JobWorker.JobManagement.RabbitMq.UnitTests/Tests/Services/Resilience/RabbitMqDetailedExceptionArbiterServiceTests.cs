using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Net.Sockets;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services.Resilience;

public class RabbitMqDetailedExceptionArbiterServiceTests
{
    private readonly RabbitMqDetailedExceptionArbiterService _sut = new();

    public static TheoryData<Exception> AccountedTransientExceptions()
    {
        return
        [
            new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "NOT_FOUND")),
            new ChannelAllocationException()
        ];
    }

    [Fact]
    public void Classification_InspectsInnerExceptions()
    {
        Assert.True(_sut.IsReasonToReconnect(new Exception("outer", new SocketException())));
        Assert.True(_sut.IsReasonToStopIfHaltOnFailure(
            new Exception("outer", new AuthenticationFailureException("ACCESS_REFUSED"))));
        Assert.True(_sut.IsAccountedForAndLikelyTransientError(
            new Exception("outer",
                new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "NOT_FOUND")))));
    }

    [Theory]
    [MemberData(nameof(AccountedTransientExceptions))]
    public void IsAccountedForAndLikelyTransientError_KnownShapes_ReturnsTrue(Exception exception)
    {
        Assert.True(_sut.IsAccountedForAndLikelyTransientError(exception));
    }

    [Fact]
    public void IsAccountedForAndLikelyTransientError_Unknown_ReturnsFalse()
    {
        Assert.False(_sut.IsAccountedForAndLikelyTransientError(new Exception("mystery")));
    }

    [Theory]
    [MemberData(nameof(ReconnectExceptions))]
    public void IsReasonToReconnect_KnownShapes_ReturnsTrue(Exception exception)
    {
        Assert.True(_sut.IsReasonToReconnect(exception));
    }

    [Fact]
    public void IsReasonToReconnect_Unknown_ReturnsFalse()
    {
        Assert.False(_sut.IsReasonToReconnect(new Exception("mystery")));
    }

    [Theory]
    [MemberData(nameof(StopIfHaltOnFailureExceptions))]
    public void IsReasonToStopIfHaltOnFailure_KnownShapes_ReturnsTrue(Exception exception)
    {
        Assert.True(_sut.IsReasonToStopIfHaltOnFailure(exception));
    }

    [Fact]
    public void IsReasonToStopIfHaltOnFailure_Unknown_ReturnsFalse()
    {
        Assert.False(_sut.IsReasonToStopIfHaltOnFailure(new Exception("mystery")));
    }

    public static TheoryData<Exception> ReconnectExceptions()
    {
        return
        [
            new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED")),
            new BrokerUnreachableException(new IOException("no broker")),
            new ConnectFailureException("connect failed", new IOException("refused")),
            new SocketException(),
            new IOException("io"),
            new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED")),
            new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 541, "INTERNAL_ERROR"))
        ];
    }

    public static TheoryData<Exception> StopIfHaltOnFailureExceptions()
    {
        return
        [
            new AuthenticationFailureException("ACCESS_REFUSED"),
            new PossibleAuthenticationFailureException("likely ACCESS_REFUSED")
        ];
    }
}