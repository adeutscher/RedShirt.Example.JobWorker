using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubMessageSourceTests
{
    private const int MaxMessagesPerRequest = 1000;

    [Fact]
    public async Task ShouldStopPagingWhenShortPageReturned()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        client.SetupSequence(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Mock<IPubSubMessageContainer>().Object, new Mock<IPubSubMessageContainer>().Object])
            .ReturnsAsync([new Mock<IPubSubMessageContainer>().Object]);

        var source = new Mock<IPubSubSubscriberClientSource>(MockBehavior.Strict);
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = new GooglePubSubMessageSource(source.Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object);

        var messages = await messageSource.GetMessagesAsync(2500, TestContext.Current.CancellationToken);

        Assert.Equal(2, messages.Count);
        source.Verify(s => s.GetSubscriberClientAsync(TestContext.Current.CancellationToken), Times.Once);
        client.Verify(c => c.GetMessagesAsync(MaxMessagesPerRequest, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(25, 50, 1)]
    [InlineData(50, 25, 1)]
    [InlineData(1000, 1000, 1)]
    [InlineData(1000, 500, 1)]
    [InlineData(0, 5, 1)]
    [InlineData(2500, 2500, 3)]
    [InlineData(2000, 2000, 2)]
    public async Task Test_GetMessagesAsync(int numberOfMessagesAvailable, int batchSize,
        int expectedPullCount)
    {
        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesAvailable);
        var remaining = numberOfMessagesAvailable;

        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int size, CancellationToken _) =>
            {
                Assert.True(size > 0, $"Invalid message count: {size}");
                Assert.True(size <= MaxMessagesPerRequest, $"Invalid message count: {size}");

                var count = Math.Min(size, remaining);
                remaining -= count;
                return Enumerable.Range(0, count)
                    .Select(_ => new Mock<IPubSubMessageContainer>().Object)
                    .ToList();
            });

        var source = new Mock<IPubSubSubscriberClientSource>(MockBehavior.Strict);
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = new GooglePubSubMessageSource(source.Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object);

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        source.Verify(s => s.GetSubscriberClientAsync(TestContext.Current.CancellationToken),
            Times.Exactly(expectedPullCount));
        client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedPullCount));
    }
}