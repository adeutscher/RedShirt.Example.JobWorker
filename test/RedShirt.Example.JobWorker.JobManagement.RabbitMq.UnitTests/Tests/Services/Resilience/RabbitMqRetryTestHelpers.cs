using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services.Resilience;

internal static class RabbitMqRetryTestHelpers
{
    public static Mock<IRabbitMqRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IRabbitMqRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<BasicGetResult?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<BasicGetResult?>>, CancellationToken>((func, token) =>
                func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IChannel>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IChannel>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}