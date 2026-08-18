using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

internal static class ActiveMqRetryTestHelpers
{
    public static Mock<IActiveMqRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IActiveMqRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<JobSourceResponse>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<JobSourceResponse>>, CancellationToken>((func, token) =>
                func(token));
        return retry;
    }
}
