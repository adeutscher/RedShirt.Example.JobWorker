using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.UnitTests.Tests.Services.Resilience;

internal static class RabbitMqRetryTestHelpers
{
    public static Mock<IRabbitMqRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IRabbitMqRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        return retry;
    }
}
