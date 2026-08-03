using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

internal static class PulsarRetryTestHelpers
{
    public static Mock<IPulsarRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IPulsarMessageContainer?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IPulsarMessageContainer?>>, CancellationToken>((func, token) =>
                func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IPulsarConsumerWrapper>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IPulsarConsumerWrapper>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}