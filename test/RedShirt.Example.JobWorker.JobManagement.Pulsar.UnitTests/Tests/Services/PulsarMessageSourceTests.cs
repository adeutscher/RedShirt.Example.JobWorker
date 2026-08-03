using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

public class PulsarMessageSourceTests
{
    private static readonly TimeSpan ExpectedConsumeTimeout = TimeSpan.FromSeconds(1);

    private static (PulsarMessageSource MessageSource, Mock<IPulsarConsumerWrapper> Consumer,
        Mock<IPulsarConsumerSource> ConsumerSource, List<IPulsarMessageContainer> QueuedMessages)
        CreateMessageSource(int availableMessages)
    {
        var queuedMessages = new List<IPulsarMessageContainer>();
        var queue = new Queue<IPulsarMessageContainer>();
        for (var i = 0; i < availableMessages; i++)
        {
            var message = CreateMessage($"msg-{i}");
            queuedMessages.Add(message);
            queue.Enqueue(message);
        }

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(ExpectedConsumeTimeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.TryDequeue(out var msg) ? msg : null);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        return (new PulsarMessageSource(consumerSource.Object,
                PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object), consumer, consumerSource,
            queuedMessages);
    }

    private static IPulsarMessageContainer CreateMessage(string messageId)
    {
        var message = new Mock<IPulsarMessageContainer>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.Value).Returns($"body-{messageId}");
        return message.Object;
    }

    [Fact]
    public async Task GetMessagesAsync_EmptyPull_ReturnsEmptyResponse()
    {
        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(ExpectedConsumeTimeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPulsarMessageContainer?) null);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object);
        var response = await messageSource.GetMessagesAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(response.Messages);
        consumer.Verify(c => c.ConsumeAsync(ExpectedConsumeTimeout, It.IsAny<CancellationToken>()), Times.Once);
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
        var (messageSource, consumer, consumerSource, queuedMessages) =
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

        consumer.Verify(c => c.ConsumeAsync(ExpectedConsumeTimeout, It.IsAny<CancellationToken>()),
            Times.Exactly(expectedConsumes));
        consumerSource.Verify(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_StopsWhenConsumeReturnsNull()
    {
        var message1 = CreateMessage("msg-0");
        var message2 = CreateMessage("msg-1");
        var consumeResults = new Queue<IPulsarMessageContainer?>([message1, message2, null]);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.ConsumeAsync(ExpectedConsumeTimeout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => consumeResults.Dequeue());

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var messageSource = new PulsarMessageSource(consumerSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object);
        var response = await messageSource.GetMessagesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Messages.Count);
        Assert.Same(message1, response.Messages[0]);
        Assert.Same(message2, response.Messages[1]);
        consumer.Verify(c => c.ConsumeAsync(ExpectedConsumeTimeout, It.IsAny<CancellationToken>()), Times.Exactly(3));
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
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            messageSource.GetMessagesAsync(1, cts.Token));

        consumer.Verify(c => c.ConsumeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}