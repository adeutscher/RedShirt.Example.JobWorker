using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;

public class KafkaMessageSourceTests
{
    private static readonly TimeSpan ExpectedConsumeTimeout = TimeSpan.FromSeconds(1);

    private static (KafkaMessageSource MessageSource, Mock<IKafkaConsumerWrapper> Consumer,
        Mock<IKafkaConsumerSource> ConsumerSource, List<IKafkaMessageContainer> QueuedMessages)
        CreateMessageSource(int availableMessages)
    {
        var queuedMessages = new List<IKafkaMessageContainer>();
        var queue = new Queue<IKafkaMessageContainer>();
        for (var i = 0; i < availableMessages; i++)
        {
            var message = CreateMessage($"msg-{i}");
            queuedMessages.Add(message);
            queue.Enqueue(message);
        }

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(ExpectedConsumeTimeout))
            .Returns(() => queue.TryDequeue(out var msg) ? msg : null);

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        return (new KafkaMessageSource(consumerSource.Object), consumer, consumerSource, queuedMessages);
    }

    private static IKafkaMessageContainer CreateMessage(string messageId)
    {
        var message = new Mock<IKafkaMessageContainer>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.Value).Returns($"body-{messageId}");
        return message.Object;
    }

    [Fact]
    public async Task GetMessagesAsync_EmptyPull_ReturnsEmptyResponse()
    {
        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(ExpectedConsumeTimeout)).Returns((IKafkaMessageContainer?) null);

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var messageSource = new KafkaMessageSource(consumerSource.Object);
        var response = await messageSource.GetMessagesAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(response.Messages);
        Assert.Null(response.LastMessage);
        consumer.Verify(c => c.Consume(ExpectedConsumeTimeout), Times.Once);
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

        if (expectedCount == 0)
        {
            Assert.Null(response.LastMessage);
        }
        else
        {
            Assert.Same(queuedMessages[expectedCount - 1], response.LastMessage);
        }

        var expectedConsumes = batchSize == 0
            ? 0
            : availableMessages >= batchSize
                ? batchSize
                : availableMessages + 1;

        consumer.Verify(c => c.Consume(ExpectedConsumeTimeout), Times.Exactly(expectedConsumes));
        consumerSource.Verify(s => s.GetConsumer(), Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_StopsWhenConsumeReturnsNull()
    {
        var message1 = CreateMessage("msg-0");
        var message2 = CreateMessage("msg-1");
        var consumeResults = new Queue<IKafkaMessageContainer?>([message1, message2, null]);

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(ExpectedConsumeTimeout))
            .Returns(() => consumeResults.Dequeue());

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var messageSource = new KafkaMessageSource(consumerSource.Object);
        var response = await messageSource.GetMessagesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Messages.Count);
        Assert.Same(message1, response.Messages[0]);
        Assert.Same(message2, response.Messages[1]);
        Assert.Same(message2, response.LastMessage);
        consumer.Verify(c => c.Consume(ExpectedConsumeTimeout), Times.Exactly(3));
    }

    [Fact]
    public async Task GetMessagesAsync_ThrowsWhenCancelledBeforeConsume()
    {
        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var messageSource = new KafkaMessageSource(consumerSource.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            messageSource.GetMessagesAsync(1, cts.Token));

        consumer.Verify(c => c.Consume(It.IsAny<TimeSpan>()), Times.Never);
    }
}