using NATS.Client.Core;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

public class NatsSubscribeExceptionArbiterServiceTests
{
    private readonly NatsSubscribeExceptionArbiterService _sut = new();

    [Fact]
    public void IsReasonToReconnect_WhenConnectionFailed_ReturnsTrue()
    {
        Assert.True(_sut.IsReasonToReconnect(new NatsConnectionFailedException("down")));
    }

    [Fact]
    public void IsReasonToStopIfHaltOnFailure_WhenAuthError_ReturnsTrue()
    {
        Assert.True(_sut.IsReasonToStopIfHaltOnFailure(new NatsServerException("Authorization Violation")));
    }
}
