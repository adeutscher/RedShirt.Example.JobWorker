using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsConsumerSourceTests
{
    [Fact]
    public async Task Test_Get()
    {
        var streamName = Guid.NewGuid().ToString();
        var mockConsumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);

        var mockContext = new Mock<INatsJSContext>(MockBehavior.Strict);
        mockContext
            .Setup(c => c.CreateOrUpdateConsumerAsync(streamName, It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        var factory = new Mock<INatsJetStreamContextFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        var source = new NatsConsumerSource(factory.Object,
            Options.Create(new NatsStreamConfigurationModel {StreamName = streamName}));

        factory.Verify(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()), Times.Never);

        var consumer = await source.GetConsumerAsync(TestContext.Current.CancellationToken);
        Assert.Same(mockConsumer.Object, consumer);

        factory.Verify(f => f.CreateNatsJetStreamContextAsync(TestContext.Current.CancellationToken), Times.Once);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(streamName,
                It.Is<ConsumerConfig>(cfg => !string.IsNullOrWhiteSpace(cfg.Name)),
                TestContext.Current.CancellationToken), Times.Once);

        var consumer2 = await source.GetConsumerAsync(TestContext.Current.CancellationToken);
        Assert.Same(consumer, consumer2);

        factory.Verify(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}