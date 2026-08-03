using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;

internal static class GooglePubSubRetryTestHelpers
{
    public static Mock<IGooglePubSubRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IGooglePubSubRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<List<IPubSubMessageContainer>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<IPubSubMessageContainer>>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}