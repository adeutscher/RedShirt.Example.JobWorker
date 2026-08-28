using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Factories;

public class BusReceiverClientSourceTests
{
    [Fact]
    public async Task Test_Get()
    {
        var wrapper = new Mock<IServiceBusClientWrapper>().Object;
        var factory = new Mock<IBusReceiverClientFactory>();
        factory.Setup(f => f.GetQueueClientAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrapper);

        var source = new BusReceiverClientSource(factory.Object);
        factory.Verify(f => f.GetQueueClientAsync(false, It.IsAny<CancellationToken>()), Times.Never);

        var response = await source.GetQueueClientAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(response.Client);
        Assert.False(response.CachedClient);
        factory.Verify(f => f.GetQueueClientAsync(false, It.IsAny<CancellationToken>()), Times.Once);

        var response2 = await source.GetQueueClientAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(response2.Client);
        Assert.True(response2.CachedClient);
        Assert.Same(response.Client, response2.Client);

        factory.Verify(f => f.GetQueueClientAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test_Get_ForceNewClient_Recreates()
    {
        var first = new Mock<IServiceBusClientWrapper>().Object;
        var second = new Mock<IServiceBusClientWrapper>().Object;
        var factory = new Mock<IBusReceiverClientFactory>();
        factory.SetupSequence(f => f.GetQueueClientAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(first)
            .ReturnsAsync(second);

        var source = new BusReceiverClientSource(factory.Object);

        var response1 = await source.GetQueueClientAsync(cancellationToken: TestContext.Current.CancellationToken);
        var response2 =
            await source.GetQueueClientAsync(true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(first, response1.Client);
        Assert.Same(second, response2.Client);
        Assert.False(response2.CachedClient);
        factory.Verify(f => f.GetQueueClientAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}