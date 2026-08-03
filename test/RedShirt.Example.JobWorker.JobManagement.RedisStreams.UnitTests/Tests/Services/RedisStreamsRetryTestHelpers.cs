using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

internal static class RedisStreamsRetryTestHelpers
{
    public static Mock<IRedisStreamsRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IRedisStreamsRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<StreamEntry[]>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<StreamEntry[]>>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<long>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<long>>, CancellationToken>((func, token) => func(token));
        return retry;
    }
}