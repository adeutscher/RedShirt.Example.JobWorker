using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Factories;

public class QueueConsumerClientSourceTests
{
    [Fact]
    public void Test_Get()
    {
        var factory = new Mock<IQueueConsumerClientFactory>();
        factory.Setup(f => f.GetQueueClient())
            .Returns(new Mock<IQueueConsumerClientWrapper>().Object);

        var source = new QueueConsumerClientSource(factory.Object);
        // Not called off the bat
        factory.Verify(f => f.GetQueueClient(), Times.Never);

        var client = source.GetQueueClient();
        Assert.NotNull(client);
        factory.Verify(f => f.GetQueueClient(), Times.Once);

        var client2 = source.GetQueueClient();
        Assert.NotNull(client2);
        Assert.Same(client, client2);

        // Still only once
        factory.Verify(f => f.GetQueueClient(), Times.Once);
    }
}