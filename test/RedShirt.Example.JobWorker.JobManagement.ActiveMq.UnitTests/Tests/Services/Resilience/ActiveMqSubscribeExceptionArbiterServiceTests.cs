using Apache.NMS;
using Apache.NMS.ActiveMQ;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;
using System.Net.Sockets;
using ActiveMqIoException = Apache.NMS.ActiveMQ.IOException;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services.Resilience;

public class ActiveMqSubscribeExceptionArbiterServiceTests
{
    private readonly ActiveMqSubscribeExceptionArbiterService _sut = new();

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

    [Fact]
    public void Classification_InspectsInnerExceptions()
    {
        Assert.True(_sut.IsReasonToReconnect(new Exception("outer", new SocketException())));
        Assert.True(_sut.IsReasonToStopIfHaltOnFailure(new Exception("outer", new NMSSecurityException("auth"))));
        Assert.True(_sut.IsAccountedForAndLikelyTransientError(new Exception("outer", new BrokerException())));
    }

    public static TheoryData<Exception> AccountedTransientExceptions()
    {
        return
        [
            new BrokerException(),
            new ResourceAllocationException("busy"),
            new TransactionRolledBackException("rollback"),
            new IllegalStateException("illegal")
        ];
    }

    public static TheoryData<Exception> StopIfHaltOnFailureExceptions()
    {
        return
        [
            new NMSSecurityException("auth"),
            new InvalidDestinationException("missing"),
            new InvalidClientIDException("client"),
            new InvalidSelectorException("selector")
        ];
    }

    public static TheoryData<Exception> ReconnectExceptions()
    {
        return
        [
            new EndOfStreamException("eof"),
            new SocketException(),
            new ActiveMqIoException("transport"),
            new IOException("io"),
            new NMSConnectionException("nms"),
            new ConnectionClosedException("closed"),
            new ConnectionFailedException("failed"),
            new ConsumerClosedException("consumer")
        ];
    }
}
