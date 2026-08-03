using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubMessageSourceTests
{
    private const int MaxMessagesPerRequest = 1000;

    [Theory]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(1000, 1000)]
    [InlineData(1000, 500)]
    [InlineData(0, 5)]
    [InlineData(2500, 2500)]
    public async Task Test_GetMessages(int numberOfMessagesAvailable, int batchSize)
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

        var source = new Mock<IPubSubSubscriberClientSource>();
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = new GooglePubSubMessageSource(source.Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object);

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        var expectedNumberOfInvocations =
            Math.Max(1,
                expectedNumberOfMessagesRetrieved / MaxMessagesPerRequest +
                (expectedNumberOfMessagesRetrieved % MaxMessagesPerRequest > 0 ? 1 : 0));

        client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedNumberOfInvocations));
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

        var messageSource = new GooglePubSubMessageSource(source.Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object);

        var messages = await messageSource.GetMessagesAsync(2500, TestContext.Current.CancellationToken);

        Assert.Equal(2, messages.Count);
        client.Verify(c => c.GetMessagesAsync(MaxMessagesPerRequest, It.IsAny<CancellationToken>()), Times.Once);
    }
}
