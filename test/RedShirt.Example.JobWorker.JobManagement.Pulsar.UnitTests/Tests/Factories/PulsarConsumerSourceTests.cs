using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Factories;

public class PulsarConsumerSourceTests
{
    [Fact]
    public async Task Test_Get()
    {
        var factory = new Mock<IPulsarConsumerFactory>();
        factory.Setup(f => f.CreateConsumerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IPulsarConsumerWrapper>().Object);

        var source = new PulsarConsumerSource(factory.Object);
        factory.Verify(f => f.CreateConsumerAsync(It.IsAny<CancellationToken>()), Times.Never);

        var client = await source.GetConsumerAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);
        factory.Verify(f => f.CreateConsumerAsync(It.IsAny<CancellationToken>()), Times.Once);

        var client2 = await source.GetConsumerAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client2);
        Assert.Same(client, client2);

        factory.Verify(f => f.CreateConsumerAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
