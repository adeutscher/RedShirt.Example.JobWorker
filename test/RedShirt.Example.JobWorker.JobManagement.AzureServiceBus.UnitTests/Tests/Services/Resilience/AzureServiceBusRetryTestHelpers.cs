using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;

internal static class AzureServiceBusRetryTestHelpers
{
    public static Mock<IAzureServiceBusRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IAzureServiceBusRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<List<IServiceBusMessageContainer>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<IServiceBusMessageContainer>>>, CancellationToken>(
                (func, token) => func(token));
        return retry;
    }
}
