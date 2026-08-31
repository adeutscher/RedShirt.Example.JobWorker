using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;
using System.Runtime.ExceptionServices;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services;

public class AzureServiceBusClientRetryWrapperTests
{
    private static (AzureServiceBusClientRetryWrapper Wrapper, Mock<IBusReceiverClientSource> Source)
        CreateWrapper(IAzureServiceBusRetryWrapperService retry, IServiceBusClientWrapper? client = null)
    {
        client ??= new Mock<IServiceBusClientWrapper>(MockBehavior.Strict).Object;
        var source = new Mock<IBusReceiverClientSource>(MockBehavior.Strict);
        var wrapper = new AzureServiceBusClientRetryWrapper(retry, source.Object);
        return (wrapper, source);
    }

    [Fact]
    public async Task GetClientAndDoActionWithRetryAsync_WhenCachedClient_ReusesClient()
    {
        var client = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper();
        var (wrapper, source) = CreateWrapper(retry, client.Object);

        source
            .SetupSequence(s => s.GetQueueClientAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = false,
                Client = client.Object
            })
            .ReturnsAsync(new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = true,
                Client = client.Object
            });

        await wrapper.GetClientAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken);
        await wrapper.GetClientAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken);

        source.Verify(s => s.GetQueueClientAsync(false, false, TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetClientAndDoActionWithRetryAsync_WhenCredentialProblem_ForcesSecretPull()
    {
        var firstClient = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var secondClient = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper(2);
        var (wrapper, source) = CreateWrapper(retry);

        source
            .Setup(s => s.GetQueueClientAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = false,
                Client = firstClient.Object
            });
        source
            .Setup(s => s.GetQueueClientAsync(true, true, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = false,
                Client = secondClient.Object
            });

        var attempts = 0;
        await wrapper.GetClientAndDoActionWithRetryAsync((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new UnauthorizedAccessException("denied");
            }

            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        source.Verify(s => s.GetQueueClientAsync(true, true, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetClientAndDoActionWithRetryAsync_WhenServiceBusCommunicationProblem_ForcesNewClient()
    {
        var firstClient = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var secondClient = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper(2);
        var (wrapper, source) = CreateWrapper(retry);

        source
            .Setup(s => s.GetQueueClientAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = false,
                Client = firstClient.Object
            });
        source
            .Setup(s => s.GetQueueClientAsync(true, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<IServiceBusClientWrapper>
            {
                CachedClient = false,
                Client = secondClient.Object
            });

        var attempts = 0;
        await wrapper.GetClientAndDoActionWithRetryAsync((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new ServiceBusException("comm", ServiceBusFailureReason.ServiceCommunicationProblem);
            }

            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        source.Verify(s => s.GetQueueClientAsync(true, false, TestContext.Current.CancellationToken), Times.Once);
    }

    private sealed class ImmediateRetryWrapper(int maxAttempts = 1) : IAzureServiceBusRetryWrapperService
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