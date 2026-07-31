using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubMessageSourceTests
{
    [Theory]
    [InlineData(5, 10, 1)]
    [InlineData(25, 10, 3)]
    [InlineData(20, 10, 2)]
    public async Task ShouldPageUntilBatchSatisfied(int batchSize, int maxPerRequest, int expectedCalls)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        client.Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int size, CancellationToken _) =>
                Enumerable.Range(0, size).Select(_ => new Mock<IPubSubMessageContainer>().Object).ToList());

        var source = new Mock<IPubSubSubscriberClientSource>();
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = new GooglePubSubMessageSource(source.Object, Options.Create(
            new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                MaxMessagesPerRequest = maxPerRequest,
                VisibilityTimeoutSeconds = 60
            }));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(batchSize, messages.Count);
        client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCalls));
    }

    [Fact]
    public async Task ShouldStopPagingWhenShortPageReturned()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        client.SetupSequence(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Mock<IPubSubMessageContainer>().Object, new Mock<IPubSubMessageContainer>().Object])
            .ReturnsAsync([new Mock<IPubSubMessageContainer>().Object]);

        var source = new Mock<IPubSubSubscriberClientSource>();
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = new GooglePubSubMessageSource(source.Object, Options.Create(
            new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                MaxMessagesPerRequest = 10,
                VisibilityTimeoutSeconds = 60
            }));

        var messages = await messageSource.GetMessagesAsync(25, TestContext.Current.CancellationToken);

        Assert.Equal(2, messages.Count);
        client.Verify(c => c.GetMessagesAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
