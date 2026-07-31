using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Factories;

public class PulsarConsumerSourceTests
{
    [Fact]
    public void Test_Get()
    {
        var factory = new Mock<IPulsarConsumerFactory>();
        factory.Setup(f => f.CreateConsumer())
            .Returns(new Mock<IPulsarConsumerWrapper>().Object);

        var source = new PulsarConsumerSource(factory.Object);
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