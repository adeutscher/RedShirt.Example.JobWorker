using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;

internal static class AzureServiceBusRetryTestHelpers
{
    public static Mock<IAzureServiceBusClientRetryWrapper> CreatePassthroughClientRetryWrapper(
        IServiceBusClientWrapper? client = null,
        IServiceBusProcessorWrapper? processor = null)
    {
        var wrapper = new Mock<IAzureServiceBusClientRetryWrapper>(MockBehavior.Strict);
        wrapper.Setup(w => w.ResetClient());
        wrapper.Setup(w => w.ResetProcessor());

        if (client is not null)
        {
            wrapper
                .Setup(w => w.GetClientAndDoActionWithRetryAsync(
                    It.IsAny<Func<IServiceBusClientWrapper, CancellationToken, Task>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<IServiceBusClientWrapper, CancellationToken, Task> callback, bool _, CancellationToken
                    token) => callback(client, token));
        }

        if (processor is not null)
        {
            wrapper
                .Setup(w => w.GetProcessorAndDoActionWithRetryAsync(
                    It.IsAny<Func<IServiceBusProcessorWrapper, CancellationToken, Task>>(),
                    It.IsAny<bool>(),
                    It.IsAny<Action<IServiceBusProcessorWrapper>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<IServiceBusProcessorWrapper, CancellationToken, Task> callback, bool _,
                    Action<IServiceBusProcessorWrapper>? onNew, CancellationToken token) =>
                {
                    onNew?.Invoke(processor);
                    return callback(processor, token);
                });
        }

        return wrapper;
    }

    public static Mock<IAzureServiceBusRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IAzureServiceBusRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<List<IServiceBusMessageContainer>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<IServiceBusMessageContainer>>>,
                CancellationToken>((func, token) => func(token));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IServiceBusClientWrapper>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IServiceBusClientWrapper>>, CancellationToken>((func, token) =>
                func(token));
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
        return retry;
    }

    public static Mock<IAzureServiceBusDetailedExceptionArbiter> CreatePermissiveDetailedArbiter()
    {
        var arbiter = new Mock<IAzureServiceBusDetailedExceptionArbiter>(MockBehavior.Strict);
        arbiter.Setup(a => a.IsReasonToReconnect(It.IsAny<Exception>())).Returns(true);
        arbiter.Setup(a => a.IsReasonToStopIfHaltOnFailure(It.IsAny<Exception>())).Returns(false);
        arbiter.Setup(a => a.IsAccountedForAndLikelyTransientError(It.IsAny<Exception>())).Returns(false);
        return arbiter;
    }
}