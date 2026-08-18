using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Services.Resilience;

internal static class AzureQueueStorageRetryTestHelpers
{
    public static Mock<IAzureQueueStorageRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IAzureQueueStorageRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<List<IQueueMessageModel>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<IQueueMessageModel>>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}
