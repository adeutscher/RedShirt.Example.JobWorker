using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsConnectionRetryWrapperTests
{
    [Fact]
    public async Task GetConsumerAndDoActionWithRetryAsync_WhenCachedConnection_ReusesConsumer()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict).Object;
        var retry = new NatsRetryTestHelpers.ImmediateRetryWrapper();
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);

        var bundle = new NatsConnectionBundle(new Mock<INatsJSContext>().Object);
        cache
            .SetupSequence(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> { CachedClient = false, Client = bundle })
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle> { CachedClient = true, Client = bundle });

        consumerSource
            .SetupSequence(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(consumer)
            .ReturnsAsync(consumer);

        var wrapper = new NatsConnectionRetryWrapper(retry, cache.Object, consumerSource.Object,
            new NatsExceptionArbiterService());

        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken);
        await wrapper.GetConsumerAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken);

        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Exactly(2));
        consumerSource.Verify(s => s.GetConsumerAsync(false, false, TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }
}
