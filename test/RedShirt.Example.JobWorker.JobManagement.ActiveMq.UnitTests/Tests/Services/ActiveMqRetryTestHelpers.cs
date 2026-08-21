using Apache.NMS;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

internal static class ActiveMqRetryTestHelpers
{
    private static void SetupPassthrough<T>(Mock<IActiveMqRetryWrapperService> retry)
    {
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<T>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<T>>, CancellationToken>((func, token) => func(token));
    }

    private static void SetupStatePassthrough(Mock<IActiveMqRetryWrapperService> retry)
    {
        retry
            .Setup(r => r.RunAsync(
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task>>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation =>
            {
                var func = (Delegate) invocation.Arguments[0];
                var state = invocation.Arguments[1];
                var token = (CancellationToken) invocation.Arguments[2];
                return (Task) func.DynamicInvoke(state, token)!;
            }));

        retry
            .Setup(r => r.RunAsync(
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<It.IsAnyType>>>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation =>
            {
                var func = (Delegate) invocation.Arguments[0];
                var state = invocation.Arguments[1];
                var token = (CancellationToken) invocation.Arguments[2];
                return (Task) func.DynamicInvoke(state, token)!;
            }));
    }

    public static Mock<IActiveMqRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IActiveMqRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));

        // ActiveMqJobSource wraps GetConsumerAsync / ReceiveAsync.
        SetupPassthrough<IMessageConsumer>(retry);
        SetupPassthrough<IMessage?>(retry);
        SetupPassthrough<JobSourceResponse>(retry);
        SetupStatePassthrough(retry);

        return retry;
    }
}