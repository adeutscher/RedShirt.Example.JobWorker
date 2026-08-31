using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;
using System.Runtime.ExceptionServices;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

internal static class NatsRetryTestHelpers
{
    private static void SetupPassthrough<T>(Mock<INatsRetryWrapperService> retry)
    {
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<T>>, CancellationToken>((func, token) => func(token));
    }

    public static Mock<INatsConnectionRetryWrapper> CreatePassthroughConnectionRetryWrapper(
        INatsJSConsumer? consumer = null)
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper.Setup(w => w.ResetConnection());

        if (consumer is not null)
        {
            wrapper
                .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                    It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                    It.IsAny<bool>(),
                    It.IsAny<Action<INatsConnection>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<INatsJSConsumer, CancellationToken, Task> callback, bool _,
                    Action<INatsConnection>? onNew,
                    CancellationToken token) => callback(consumer, token));
        }

        return wrapper;
    }

    public static Mock<INatsRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<INatsRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));

        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<It.IsAnyType, CancellationToken, Task>>(),
                It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation =>
            {
                var func = (Delegate) invocation.Arguments[0];
                var state = invocation.Arguments[1];
                var token = (CancellationToken) invocation.Arguments[2];
                return func.DynamicInvoke(state, token) as Task ?? Task.CompletedTask;
            }));

        SetupPassthrough<INatsJSMsg<NatsMemoryOwner<byte>>?>(retry);
        SetupPassthrough<List<INatsJSMsg<NatsMemoryOwner<byte>>>>(retry);

        return retry;
    }

    public sealed class ImmediateRetryWrapper(int maxAttempts = 1) : INatsRetryWrapperService
    {
        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func,
            CancellationToken cancellationToken = default)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await func(cancellationToken);
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            ExceptionDispatchInfo.Capture(last!).Throw();
            throw new InvalidOperationException();
        }

        public async Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
        {
            await RunAsync(async ct =>
            {
                await func(ct);
                return true;
            }, cancellationToken);
        }

        public async Task RunAsync<TState>(Func<TState, CancellationToken, Task> func, TState state,
            CancellationToken cancellationToken = default)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await func(state, cancellationToken);
                    return;
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            ExceptionDispatchInfo.Capture(last!).Throw();
        }
    }
}