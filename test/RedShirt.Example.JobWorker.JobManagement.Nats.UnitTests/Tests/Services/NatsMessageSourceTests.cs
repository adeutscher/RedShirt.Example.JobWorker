using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsMessageSourceTests
{
    private static readonly TimeSpan ExpectedIdleHeartbeat = TimeSpan.FromSeconds(5);

    private static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static Mock<INatsJSConsumer> CreateConsumerMock()
    {
        return new Mock<INatsJSConsumer>(MockBehavior.Strict);
    }

    private static INatsJSMsg<NatsMemoryOwner<byte>> CreateMessage(int index)
    {
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        message.Setup(m => m.Metadata).Returns(new NatsJSMsgMetadata(
            new NatsJSSequencePair((ulong) (index + 1), (ulong) (index + 1)),
            1,
            0,
            DateTimeOffset.UtcNow,
            "stream",
            "c1",
            string.Empty));
        return message.Object;
    }

    private static (NatsMessageSource MessageSource, Mock<INatsJSConsumer> Consumer, Mock<INatsConsumerSource>
        ConsumerSource) CreateSut(int waitTimeSeconds)
    {
        var consumer = CreateConsumerMock();
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        consumerSource
            .Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumer.Object);

        var messageSource = new NatsMessageSource(consumerSource.Object,
            Options.Create(new NatsMessageSource.ConfigurationModel
            {
                WaitTimeSeconds = waitTimeSeconds
            }));

        return (messageSource, consumer, consumerSource);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    public void ConfigurationModel_EffectiveWaitTimeSeconds_FloorsAtZero(int waitTimeSeconds, int expected)
    {
        var configuration = new NatsMessageSource.ConfigurationModel
        {
            WaitTimeSeconds = waitTimeSeconds
        };

        Assert.Equal(expected, configuration.EffectiveWaitTimeSeconds);
    }

    [Fact]
    public async Task FetchMessagesAsync_NoWait_ReturnsEmptyWhenFetchReturnsNothing()
    {
        var (messageSource, consumer, consumerSource) = CreateSut(0);
        consumer
            .Setup(c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(Array.Empty<INatsJSMsg<NatsMemoryOwner<byte>>>()));

        var response = await messageSource.FetchMessagesAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(response.Messages);
        consumerSource.Verify(s => s.GetConsumerAsync(TestContext.Current.CancellationToken), Times.Once);
        consumer.Verify(
            c => c.FetchNoWaitAsync<NatsMemoryOwner<byte>>(
                It.Is<NatsJSFetchOpts>(o => o.MaxMsgs == 3 && o.IdleHeartbeat == ExpectedIdleHeartbeat),
                null,
                TestContext.Current.CancellationToken),
            Times.Once);
        consumer.Verify(
            c => c.NextAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSNextOpts>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 5)]
    [InlineData(2, 2)]
    public async Task FetchMessagesAsync_NoWait_ReturnsMessagesUpToBatchSize(int availableMessages, int batchSize)
    {
        var messages = Enumerable.Range(0, availableMessages).Select(CreateMessage).ToList();
        var (messageSource, consumer, consumerSource) = CreateSut(0);
        consumer
            .Setup(c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((NatsJSFetchOpts opts, INatsDeserialize<NatsMemoryOwner<byte>> _, CancellationToken _) =>
                AsAsyncEnumerable(messages.Take(opts.MaxMsgs ?? 0)));

        var response = await messageSource.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        var expectedCount = Math.Min(availableMessages, batchSize);
        Assert.Equal(expectedCount, response.Messages.Count);
        for (var i = 0; i < expectedCount; i++)
        {
            Assert.Same(messages[i], response.Messages[i]);
        }

        consumerSource.Verify(s => s.GetConsumerAsync(TestContext.Current.CancellationToken), Times.Once);
        consumer.Verify(
            c => c.FetchNoWaitAsync<NatsMemoryOwner<byte>>(
                It.Is<NatsJSFetchOpts>(o => o.MaxMsgs == batchSize && o.IdleHeartbeat == ExpectedIdleHeartbeat),
                null,
                TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FetchMessagesAsync_NonPositiveDelayUsesFetchNoWait(int delayTimeSeconds)
    {
        var message = CreateMessage(0);
        var (messageSource, consumer, consumerSource) = CreateSut(delayTimeSeconds);
        consumer
            .Setup(c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(new[] {message}));

        var response = await messageSource.FetchMessagesAsync(1, TestContext.Current.CancellationToken);

        Assert.Single(response.Messages);
        Assert.Same(message, response.Messages[0]);
        consumerSource.Verify(s => s.GetConsumerAsync(TestContext.Current.CancellationToken), Times.Once);
        consumer.Verify(
            c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        consumer.Verify(
            c => c.NextAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSNextOpts>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(10, 5, 4)]
    public async Task FetchMessagesAsync_WithDelay_FetchesRemainingMessagesWithoutWaiting(
        int delayTimeSeconds, int batchSize, int remainingMessages)
    {
        var firstMessage = CreateMessage(0);
        var remaining = Enumerable.Range(1, remainingMessages).Select(CreateMessage).ToList();
        var (messageSource, consumer, consumerSource) = CreateSut(delayTimeSeconds);
        consumer
            .Setup(c => c.NextAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSNextOpts>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstMessage);
        consumer
            .Setup(c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((NatsJSFetchOpts opts, INatsDeserialize<NatsMemoryOwner<byte>> _, CancellationToken _) =>
                AsAsyncEnumerable(remaining.Take(opts.MaxMsgs ?? 0)));

        var response = await messageSource.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        var expectedCount = Math.Min(batchSize, 1 + remainingMessages);
        Assert.Equal(expectedCount, response.Messages.Count);
        Assert.Same(firstMessage, response.Messages[0]);
        for (var i = 1; i < expectedCount; i++)
        {
            Assert.Same(remaining[i - 1], response.Messages[i]);
        }

        consumerSource.Verify(s => s.GetConsumerAsync(TestContext.Current.CancellationToken), Times.Once);
        consumer.Verify(
            c => c.NextAsync<NatsMemoryOwner<byte>>(
                null,
                It.Is<NatsJSNextOpts>(o =>
                    o.Expires == TimeSpan.FromSeconds(delayTimeSeconds) &&
                    o.IdleHeartbeat == ExpectedIdleHeartbeat),
                TestContext.Current.CancellationToken),
            Times.Once);
        consumer.Verify(
            c => c.FetchNoWaitAsync<NatsMemoryOwner<byte>>(
                It.Is<NatsJSFetchOpts>(o => o.MaxMsgs == batchSize - 1 && o.IdleHeartbeat == ExpectedIdleHeartbeat),
                null,
                TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task FetchMessagesAsync_WithDelay_ReturnsEmptyWhenNextReturnsNull(int delayTimeSeconds)
    {
        var (messageSource, consumer, consumerSource) = CreateSut(delayTimeSeconds);
        consumer
            .Setup(c => c.NextAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSNextOpts>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((INatsJSMsg<NatsMemoryOwner<byte>>?) null);

        var response = await messageSource.FetchMessagesAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(response.Messages);
        consumerSource.Verify(s => s.GetConsumerAsync(TestContext.Current.CancellationToken), Times.Once);
        consumer.Verify(
            c => c.NextAsync<NatsMemoryOwner<byte>>(
                null,
                It.Is<NatsJSNextOpts>(o =>
                    o.Expires == TimeSpan.FromSeconds(delayTimeSeconds) &&
                    o.IdleHeartbeat == ExpectedIdleHeartbeat),
                TestContext.Current.CancellationToken),
            Times.Once);
        consumer.Verify(
            c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    public async Task FetchMessagesAsync_WithDelay_ReturnsSingleMessageFromNext(int delayTimeSeconds, int batchSize)
    {
        var message = CreateMessage(0);
        var (messageSource, consumer, consumerSource) = CreateSut(delayTimeSeconds);
        consumer
            .Setup(c => c.NextAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSNextOpts>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        consumer
            .Setup(c => c.FetchNoWaitAsync(
                It.IsAny<NatsJSFetchOpts>(),
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(Array.Empty<INatsJSMsg<NatsMemoryOwner<byte>>>()));

        var response = await messageSource.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Single(response.Messages);
        Assert.Same(message, response.Messages[0]);
        consumerSource.Verify(s => s.GetConsumerAsync(TestContext.Current.CancellationToken), Times.Once);
        consumer.Verify(
            c => c.FetchNoWaitAsync<NatsMemoryOwner<byte>>(
                It.Is<NatsJSFetchOpts>(o => o.MaxMsgs == 0 && o.IdleHeartbeat == ExpectedIdleHeartbeat),
                null,
                TestContext.Current.CancellationToken),
            Times.Once);
    }
}