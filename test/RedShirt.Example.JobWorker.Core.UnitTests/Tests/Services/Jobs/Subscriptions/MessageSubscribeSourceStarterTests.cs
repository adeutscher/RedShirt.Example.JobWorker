using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs.Subscriptions;

public class MessageSubscribeSourceStarterTests
{
    [Fact]
    public async Task RunAsync_WhenNotSubscriptionSource_DoesNotStartSubscriber()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(false);

        var starter = new MessageSubscribeSourceStarter(jobSource.Object);

        var result = await starter.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Bootstrap, result);
        jobSource.Verify(s => s.StartSubscriberAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenSubscriptionSource_StartsSubscriberAndReturnsBootstrap()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(true);
        jobSource
            .Setup(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var starter = new MessageSubscribeSourceStarter(jobSource.Object);

        var result = await starter.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Bootstrap, result);
        jobSource.Verify(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}