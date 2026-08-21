using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

internal static class NatsRetryTestHelpers
{
    public static Mock<INatsRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<INatsRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<NatsMessageSourceResponse>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<NatsMessageSourceResponse>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}