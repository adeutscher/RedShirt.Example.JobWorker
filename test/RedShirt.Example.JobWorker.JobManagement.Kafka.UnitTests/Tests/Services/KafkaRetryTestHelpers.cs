using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;

internal static class KafkaRetryTestHelpers
{
    public static Mock<IKafkaRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IKafkaMessageContainer?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IKafkaMessageContainer?>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}