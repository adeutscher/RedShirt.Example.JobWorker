using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Factories;

public class KafkaConsumerSourceTests
{
    [Fact]
    public void Test_Get()
    {
        var factory = new Mock<IKafkaConsumerFactory>();
        factory.Setup(f => f.CreateConsumer())
            .Returns(new Mock<IKafkaConsumerWrapper>().Object);

        var source = new KafkaConsumerSource(factory.Object);
        factory.Verify(f => f.CreateConsumer(), Times.Never);

        var client = source.GetConsumer();
        Assert.NotNull(client);
        factory.Verify(f => f.CreateConsumer(), Times.Once);

        var client2 = source.GetConsumer();
        Assert.NotNull(client2);
        Assert.Same(client, client2);

        factory.Verify(f => f.CreateConsumer(), Times.Once);
    }
}