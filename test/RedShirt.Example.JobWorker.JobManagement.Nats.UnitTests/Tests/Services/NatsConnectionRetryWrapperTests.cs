using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsConnectionRetryWrapperTests
{
    private static NatsConnectionBundle CreateBundle()
    {
        var context = new Mock<INatsJSContext>();
        context.SetupGet(c => c.Connection).Returns(new Mock<INatsConnection>().Object);
        return new NatsConnectionBundle(context.Object);
    }

    private static NatsConnectionRetryWrapper CreateWrapper(
        INatsRetryWrapperService retry,
        Mock<INatsConnectionCacheSource> cache,
        Mock<INatsConsumerSource> consumerSource,
        INatsExceptionArbiterService? exceptionArbiter = null)
    {
        return new NatsConnectionRetryWrapper(retry, cache.Object, consumerSource.Object,
            exceptionArbiter ?? new NatsExceptionArbiterService());
    }

    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenCachedClient_DoesNotInvokeOnNewConnectionCallback()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper();
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        var bundle = CreateBundle();

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = true, Client = bundle});
        consumerSource
            .Setup(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer);

        var callbackInvoked = false;
        var wrapper = CreateWrapper(retry, cache, consumerSource);

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            onNewConnectionCallback: _ => callbackInvoked = true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenCachedConnection_ReusesConsumer()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper();
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);

        var bundle = CreateBundle();
        cache
            .SetupSequence(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = false, Client = bundle})
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = true, Client = bundle});

        consumerSource
            .SetupSequence(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer)
            .ReturnsAsync(consumer);

        var wrapper = CreateWrapper(retry, cache, consumerSource);

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken);
        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken);

        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Exactly(2));
        consumerSource.Verify(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenForceNewConnectionImmediately_PassesForceFlags()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper();
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        var bundle = CreateBundle();

        cache
            .Setup(c => c.GetConnectionAsync(true, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = false, Client = bundle});
        consumerSource
            .Setup(s => s.GetConsumerAsync(true, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer);

        var wrapper = CreateWrapper(retry, cache, consumerSource);

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            true,
            cancellationToken: TestContext.Current.CancellationToken);

        cache.Verify(c => c.GetConnectionAsync(true, false, TestContext.Current.CancellationToken), Times.Once);
        consumerSource.Verify(s => s.GetConsumerAsync(true, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenNewConnection_InvokesOnNewConnectionCallback()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper();
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        var bundle = CreateBundle();

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = false, Client = bundle});
        consumerSource
            .Setup(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer);

        INatsConnection? seenConnection = null;
        var wrapper = CreateWrapper(retry, cache, consumerSource);

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            onNewConnectionCallback: connection => seenConnection = connection,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(bundle.Connection, seenConnection);
    }

    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenNonTransientFailure_DoesNotForceNewConnection()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper(2);
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        var bundle = CreateBundle();
        var attempts = 0;

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = true, Client = bundle});
        consumerSource
            .Setup(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer);
        consumerSource.Setup(s => s.ResetConsumer());

        var wrapper = CreateWrapper(retry, cache, consumerSource);

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new ArgumentException("bad argument");
            }

            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Exactly(2));
        cache.Verify(c => c.GetConnectionAsync(true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        consumerSource.Verify(s => s.ResetConsumer(), Times.Once);
    }

    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenTransientFailure_RetriesWithNewConnection()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper(2);
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        var bundle = CreateBundle();
        var attempts = 0;

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = true, Client = bundle});
        cache
            .Setup(c => c.GetConnectionAsync(true, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> {CachedClient = false, Client = bundle});

        consumerSource
            .Setup(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer);
        consumerSource
            .Setup(s => s.GetConsumerAsync(true, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer);
        consumerSource.Setup(s => s.ResetConsumer());

        var wrapper = CreateWrapper(retry, cache, consumerSource);

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new NatsTimeoutException();
            }

            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Once);
        cache.Verify(c => c.GetConnectionAsync(true, false, TestContext.Current.CancellationToken), Times.Once);
        consumerSource.Verify(s => s.ResetConsumer(), Times.Once);
    }

    [Fact]
    public void ResetConnection_ResetsConsumerSource()
    {
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper();
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.ResetConsumer());

        var wrapper = CreateWrapper(retry, cache, consumerSource);

        wrapper.ResetConnection();

        consumerSource.Verify(s => s.ResetConsumer(), Times.Once);
    }
}