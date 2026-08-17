using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

public class PulsarMessageSourceTests
{
    private static readonly TimeSpan FollowUpConsumeTimeout =
        TimeSpan.FromMilliseconds(PulsarMessageSource.FollowUpShortPollConsumeTimeoutMilliseconds);

    private static IOptions<PulsarMessageSource.ConfigurationModel> CreateOptions(int waitTimeSeconds = 1)
    {
        return Options.Create(new PulsarMessageSource.ConfigurationModel
        {
            WaitTimeSeconds = waitTimeSeconds
        });
    }

    private static TimeSpan ExpectedFirstConsumeTimeout(int waitTimeSeconds = 1)
    {
        var effectiveWaitTimeSeconds = Math.Max(0, waitTimeSeconds);
        return effectiveWaitTimeSeconds > 0
            ? TimeSpan.FromSeconds(effectiveWaitTimeSeconds)
            : TimeSpan.FromMilliseconds(PulsarMessageSource.InitialShortPollConsumeTimeoutMilliseconds);
    }

    private static (PulsarMessageSource MessageSource, Mock<IPulsarConsumerWrapper> Consumer,
        Mock<IPulsarConsumerSource> ConsumerSource, List<IPulsarMessageContainer> QueuedMessages,
        List<TimeSpan> ConsumeTimeouts)
        CreateMessageSource(int availableMessages, int waitTimeSeconds = 1)
    {
        var queuedMessages = new List<IPulsarMessageContainer>();
        var queue = new Queue<IPulsarMessageContainer>();
        for (var i = 0; i < availableMessages; i++)
        {
            var message = CreateMessage($"msg-{i}");
            queuedMessages.Add(message);
            queue.Enqueue(message);
        }

        var consumeTimeouts = new List<TimeSpan>();
        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeSpan timeout, CancellationToken _) =>
            {
                consumeTimeouts.Add(timeout);
                return queue.TryDequeue(out var msg) ? msg : null;
            });

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        return (new PulsarMessageSource(consumerSource.Object,
                PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
                CreateOptions(waitTimeSeconds)), consumer, consumerSource,
            queuedMessages, consumeTimeouts);
    }

    private static IPulsarMessageContainer CreateMessage(string messageId)
    {
        var message = new Mock<IPulsarMessageContainer>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.Value).Returns($"body-{messageId}");
        return message.Object;
    }

    private static void AssertConsumeTimeouts(IReadOnlyList<TimeSpan> consumeTimeouts, int expectedConsumes,
        int waitTimeSeconds = 1)
    {
        Assert.Equal(expectedConsumes, consumeTimeouts.Count);
        if (expectedConsumes == 0)
        {
            return;
        }

        Assert.Equal(ExpectedFirstConsumeTimeout(waitTimeSeconds), consumeTimeouts[0]);
        Assert.All(consumeTimeouts.Skip(1), timeout => Assert.Equal(FollowUpConsumeTimeout, timeout));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    public void ConfigurationModel_EffectiveWaitTimeSeconds_FloorsAtZero(int configured, int expected)
    {
        var model = new PulsarMessageSource.ConfigurationModel
        {
            WaitTimeSeconds = configured
        };

        Assert.Equal(expected, model.EffectiveWaitTimeSeconds);
    }

    [Fact]
    public async Task GetMessagesAsync_EmptyPull_ReturnsEmptyResponse()
    {
        var consumeTimeouts = new List<TimeSpan>();
        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeSpan timeout, CancellationToken _) =>
            {
                consumeTimeouts.Add(timeout);
                return null;
            });

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreateOptions());
        var response = await messageSource.GetMessagesAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(response.Messages);
        AssertConsumeTimeouts(consumeTimeouts, 1);
        consumer.Verify(c => c.ConsumeAsync(ExpectedFirstConsumeTimeout(), It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.ConsumeAsync(FollowUpConsumeTimeout, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    public async Task GetMessagesAsync_PassesEffectiveWaitTimeSecondsToFirstConsume(int waitTimeSeconds)
    {
        var expectedTimeout = ExpectedFirstConsumeTimeout(waitTimeSeconds);
        var consumeTimeouts = new List<TimeSpan>();
        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeSpan timeout, CancellationToken _) =>
            {
                consumeTimeouts.Add(timeout);
                return null;
            });

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreateOptions(waitTimeSeconds));

        await messageSource.GetMessagesAsync(1, TestContext.Current.CancellationToken);

        AssertConsumeTimeouts(consumeTimeouts, 1, waitTimeSeconds);
        consumer.Verify(c => c.ConsumeAsync(expectedTimeout, It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.ConsumeAsync(FollowUpConsumeTimeout, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 5)]
    [InlineData(0, 5)]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public async Task GetMessagesAsync_ReturnsMessagesUpToBatchSize(int availableMessages, int batchSize)
    {
        var expectedCount = Math.Min(availableMessages, batchSize);
        var (messageSource, consumer, consumerSource, queuedMessages, consumeTimeouts) =
            CreateMessageSource(availableMessages);

        var response = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(expectedCount, response.Messages.Count);

        for (var i = 0; i < expectedCount; i++)
        {
            Assert.Same(queuedMessages[i], response.Messages[i]);
        }

        var expectedConsumes = batchSize == 0
            ? 0
            : availableMessages >= batchSize
                ? batchSize
                : availableMessages + 1;

        AssertConsumeTimeouts(consumeTimeouts, expectedConsumes);
        consumer.Verify(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedConsumes));
        consumerSource.Verify(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_StopsWhenConsumeReturnsNull()
    {
        var message1 = CreateMessage("msg-0");
        var message2 = CreateMessage("msg-1");
        var consumeResults = new Queue<IPulsarMessageContainer?>([message1, message2, null]);
        var consumeTimeouts = new List<TimeSpan>();

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeSpan timeout, CancellationToken _) =>
            {
                consumeTimeouts.Add(timeout);
                return consumeResults.Dequeue();
            });

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreateOptions());
        var response = await messageSource.GetMessagesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Messages.Count);
        Assert.Same(message1, response.Messages[0]);
        Assert.Same(message2, response.Messages[1]);
        AssertConsumeTimeouts(consumeTimeouts, 3);
        consumer.Verify(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task GetMessagesAsync_ThrowsWhenCancelledBeforeConsume()
    {
        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreateOptions());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            messageSource.GetMessagesAsync(1, cts.Token));

        consumer.Verify(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMessagesAsync_UsesShortTimeoutForFollowUpConsumes()
    {
        const int waitTimeSeconds = 5;
        var message1 = CreateMessage("msg-0");
        var message2 = CreateMessage("msg-1");
        var consumeResults = new Queue<IPulsarMessageContainer?>([message1, message2, null]);
        var consumeTimeouts = new List<TimeSpan>();

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeSpan timeout, CancellationToken _) =>
            {
                consumeTimeouts.Add(timeout);
                return consumeResults.Dequeue();
            });

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreateOptions(waitTimeSeconds));
        var response = await messageSource.GetMessagesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Messages.Count);
        AssertConsumeTimeouts(consumeTimeouts, 3, waitTimeSeconds);
        consumer.Verify(c => c.ConsumeAsync(TimeSpan.FromSeconds(waitTimeSeconds), It.IsAny<CancellationToken>()),
            Times.Once);
        consumer.Verify(c => c.ConsumeAsync(FollowUpConsumeTimeout, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetMessagesAsync_ZeroWait_UsesInitialMillisecondTimeoutThenFollowUp()
    {
        var message1 = CreateMessage("msg-0");
        var message2 = CreateMessage("msg-1");
        var consumeResults = new Queue<IPulsarMessageContainer?>([message1, message2, null]);
        var consumeTimeouts = new List<TimeSpan>();

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeSpan timeout, CancellationToken _) =>
            {
                consumeTimeouts.Add(timeout);
                return consumeResults.Dequeue();
            });

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreateOptions(0));
        var response = await messageSource.GetMessagesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Messages.Count);
        AssertConsumeTimeouts(consumeTimeouts, 3, 0);
        consumer.Verify(
            c => c.ConsumeAsync(
                TimeSpan.FromMilliseconds(PulsarMessageSource.InitialShortPollConsumeTimeoutMilliseconds),
                It.IsAny<CancellationToken>()),
            Times.Once);
        consumer.Verify(c => c.ConsumeAsync(FollowUpConsumeTimeout, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}