using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubMessageSourceTests
{
    private const int MaxMessagesPerRequest = 1000;

    private static GooglePubSubMessageSource CreateMessageSource(IPubSubSubscriberClientSource source,
        int waitTimeSeconds = 1)
    {
        return new GooglePubSubMessageSource(source,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                VisibilityTimeoutSeconds = 60,
                WaitTimeSeconds = waitTimeSeconds,
                DlqNotEnabled = true,
                MaximumReceives = 3
            }));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(120, 60)]
    public async Task GetMessagesAsync_PassesEffectiveWaitTimeSeconds(int configuredWaitTimeSeconds,
        int expectedWaitTimeSeconds)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var source = new Mock<IPubSubSubscriberClientSource>(MockBehavior.Strict);
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = CreateMessageSource(source.Object, configuredWaitTimeSeconds);

        await messageSource.GetMessagesAsync(5, TestContext.Current.CancellationToken);

        client.Verify(
            c => c.GetMessagesAsync(5, expectedWaitTimeSeconds, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task ShouldStopPagingWhenShortPageReturned()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        client.SetupSequence(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Mock<IPubSubMessageContainer>().Object, new Mock<IPubSubMessageContainer>().Object])
            .ReturnsAsync([new Mock<IPubSubMessageContainer>().Object]);

        var source = new Mock<IPubSubSubscriberClientSource>(MockBehavior.Strict);
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var messageSource = CreateMessageSource(source.Object);

        var messages = await messageSource.GetMessagesAsync(2500, TestContext.Current.CancellationToken);

        Assert.Equal(2, messages.Count);
        source.Verify(s => s.GetSubscriberClientAsync(TestContext.Current.CancellationToken), Times.Once);
        client.Verify(c => c.GetMessagesAsync(MaxMessagesPerRequest, 1, TestContext.Current.CancellationToken),
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
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int size, int _, CancellationToken _) =>
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

        var messageSource = CreateMessageSource(source.Object);

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        source.Verify(s => s.GetSubscriberClientAsync(TestContext.Current.CancellationToken),
            Times.Exactly(expectedPullCount));
        // Any call
        client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedPullCount));
    }

    [Theory]
    [InlineData(25, 50, 1, 0)]
    [InlineData(50, 25, 1, 0)]
    [InlineData(1000, 1000, 1, 0)]
    [InlineData(1000, 500, 1, 0)]
    [InlineData(0, 5, 1, 10)]
    [InlineData(2500, 2500, 3, 10)]
    [InlineData(2000, 2000, 2, 10)]
    public async Task Test_GetMessagesAsync_PlusWaitTimeEnforcement(int numberOfMessagesAvailable, int batchSize,
        int expectedPullCount, int waitTime)
    {
        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesAvailable);
        var remaining = numberOfMessagesAvailable;

        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int size, int _, CancellationToken _) =>
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

        var messageSource = CreateMessageSource(source.Object, waitTime);

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        source.Verify(s => s.GetSubscriberClientAsync(TestContext.Current.CancellationToken),
            Times.Exactly(expectedPullCount));
        // Any call
        client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedPullCount));
        // Called with wait time only once
        if (waitTime <= 0)
        {
            // For waitTime <= 0, every call is expected to have no wait time (0).
            client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), waitTime, TestContext.Current.CancellationToken),
                Times.Exactly(expectedPullCount));
        }
        else
        {
            // For waitTime > 0, only the first call is expected to have wait time.
            client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), waitTime, TestContext.Current.CancellationToken),
                Times.Once);
            // For waitTime > 0, follow-up calls are expected to have no wait time (0).
            var expectedFollowUpCount = expectedPullCount - 1;
            if (expectedFollowUpCount > 0)
            {
                client.Verify(c => c.GetMessagesAsync(It.IsAny<int>(), 0, TestContext.Current.CancellationToken),
                    Times.Exactly(expectedFollowUpCount));
            }
        }
    }
}