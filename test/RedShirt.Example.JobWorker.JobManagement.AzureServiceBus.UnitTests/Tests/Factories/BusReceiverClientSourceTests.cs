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
        factory.Setup(f => f.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrapper);

        var source = new BusReceiverClientSource(factory.Object);
        // Not called off the bat
        factory.Verify(f => f.GetQueueClientAsync(It.IsAny<CancellationToken>()), Times.Never);

        var client = await source.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);
        factory.Verify(f => f.GetQueueClientAsync(It.IsAny<CancellationToken>()), Times.Once);

        var client2 = await source.GetQueueClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client2);
        Assert.Same(client, client2);

        // Still only once
        factory.Verify(f => f.GetQueueClientAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}