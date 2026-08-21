using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

internal static class NatsRetryTestHelpers
{
    private static void SetupPassthrough<T>(Mock<INatsRetryWrapperService> retry)
    {
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<T>>, CancellationToken>((func, token) => func(token));
    }

    public static Mock<INatsRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<INatsRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));

        // NatsMessageSource wraps GetConsumerAsync / NextAsync / FetchNoWait batching.
        SetupPassthrough<INatsJSConsumer>(retry);
        SetupPassthrough<INatsJSMsg<NatsMemoryOwner<byte>>?>(retry);
        SetupPassthrough<List<INatsJSMsg<NatsMemoryOwner<byte>>>>(retry);
        SetupPassthrough<NatsMessageSourceResponse>(retry);

        return retry;
    }
}