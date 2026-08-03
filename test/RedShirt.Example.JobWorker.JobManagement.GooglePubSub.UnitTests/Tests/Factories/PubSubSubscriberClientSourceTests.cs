using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Factories;

public class PubSubSubscriberClientSourceTests
{
    [Fact]
    public void GetSubscriberClientAsync_LazilyCachesFactoryResult()
    {
        var factory = new Mock<IPubSubSubscriberClientFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IPubSubSubscriberClientWrapper>().Object);

        var source = new PubSubSubscriberClientSource(factory.Object);

        factory.Verify(f => f.GetSubscriberClientAsync(It.IsAny<CancellationToken>()), Times.Never);

        var client = source.GetSubscriberClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client);
        factory.Verify(f => f.GetSubscriberClientAsync(It.IsAny<CancellationToken>()), Times.Once);

        var client2 = source.GetSubscriberClientAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(client2);
        Assert.Same(client, client2);

        factory.Verify(f => f.GetSubscriberClientAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}