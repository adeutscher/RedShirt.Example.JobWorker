using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsConsumerSourceTests
{
    [Theory]
    [InlineData("foo")]
    [InlineData("bar")]
    public async Task Test_Get(string consumerName)
    {
        var streamName = Guid.NewGuid().ToString();
        var mockConsumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var mockContext = new Mock<INatsJSContext>(MockBehavior.Strict);
        mockContext
            .Setup(c => c.CreateOrUpdateConsumerAsync(streamName, It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        var bundle = new NatsConnectionBundle(mockContext.Object);
        var cache = new Mock<INatsConnectionCacheSource>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetConnectionAsync(false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientCacheResponse<NatsConnectionBundle>
            {
                CachedClient = false,
                Client = bundle
            });

        var source = new NatsConsumerSource(cache.Object,
            Options.Create(new NatsStreamConfigurationModel
            {
                StreamName = streamName,
                ConsumerName = consumerName
            }),
            Options.Create(new NatsStreamTimeoutConfigurationModel
            {
                VisibilityTimeoutSeconds = 40
            }));

        cache.Verify(c => c.GetConnectionAsync(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var consumer = await source.GetConsumerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Same(mockConsumer.Object, consumer);

        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Once);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(streamName,
                It.Is<ConsumerConfig>(cfg =>
                    cfg.Name == consumerName
                    && cfg.DurableName == consumerName
                    && cfg.AckWait == TimeSpan.FromSeconds(40)),
                TestContext.Current.CancellationToken), Times.Once);

        var consumer2 = await source.GetConsumerAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Same(consumer, consumer2);

        cache.Verify(c => c.GetConnectionAsync(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}