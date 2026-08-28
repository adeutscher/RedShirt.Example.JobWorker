using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;
using System.Net.Sockets;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;

public class AzureServiceBusDetailedExceptionArbiterServiceTests
{
    private readonly AzureServiceBusDetailedExceptionArbiterService _sut = new();

    [Fact]
    public void Classification_InspectsInnerExceptions()
    {
        Assert.True(_sut.IsReasonToReconnect(new Exception("outer", new SocketException())));
        Assert.True(_sut.IsReasonToStopIfHaltOnFailure(
            new Exception("outer", new UnauthorizedAccessException())));
        Assert.True(_sut.IsAccountedForAndLikelyTransientError(
            new Exception("outer",
                new ServiceBusException("lock", ServiceBusFailureReason.MessageLockLost))));
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

    [Theory]
    [MemberData(nameof(AccountedTransientExceptions))]
    public void IsAccountedForAndLikelyTransientError_KnownShapes_ReturnsTrue(Exception exception)
    {
        Assert.True(_sut.IsAccountedForAndLikelyTransientError(exception));
    }

    public static TheoryData<Exception> ReconnectExceptions()
    {
        return
        [
            new SocketException(),
            new IOException("io"),
            new ServiceBusException("timeout", ServiceBusFailureReason.ServiceTimeout),
            new ServiceBusException("busy", ServiceBusFailureReason.ServiceBusy),
            new ServiceBusException("comm", ServiceBusFailureReason.ServiceCommunicationProblem)
        ];
    }

    public static TheoryData<Exception> StopIfHaltOnFailureExceptions()
    {
        return
        [
            new UnauthorizedAccessException(),
            new ServiceBusException("missing", ServiceBusFailureReason.MessagingEntityNotFound),
            new ServiceBusException("disabled", ServiceBusFailureReason.MessagingEntityDisabled)
        ];
    }

    public static TheoryData<Exception> AccountedTransientExceptions()
    {
        return
        [
            new ServiceBusException("lock", ServiceBusFailureReason.MessageLockLost),
            new ObjectDisposedException("processor")
        ];
    }
}
