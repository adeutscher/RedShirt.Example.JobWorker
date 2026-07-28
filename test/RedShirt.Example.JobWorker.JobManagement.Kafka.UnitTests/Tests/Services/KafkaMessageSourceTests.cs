using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;

public class KafkaMessageSourceTests
{
    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 5)]
    [InlineData(0, 5)]
    [InlineData(2, 2)]
    public async Task Test_GetMessages(int availableMessages, int batchSize)
    {
        var expected = Math.Min(availableMessages, batchSize);
        var queue = new Queue<IKafkaMessageContainer>();
        for (var i = 0; i < availableMessages; i++)
        {
            var message = new Mock<IKafkaMessageContainer>();
            message.SetupGet(m => m.MessageId).Returns($"msg-{i}");
            message.SetupGet(m => m.Value).Returns($"body-{i}");
            queue.Enqueue(message.Object);
        }

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns(() => queue.TryDequeue(out var msg) ? msg : null);

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var messageSource = new KafkaMessageSource(consumerSource.Object);
        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(expected, messages.Count);

        // One extra Consume call that returns null when the queue empties before batchSize,
        // or exactly batchSize Consumes when the queue has enough.
        var expectedConsumes = availableMessages >= batchSize ? batchSize : availableMessages + (batchSize > 0 ? 1 : 0);
        if (batchSize == 0)
        {
            expectedConsumes = 0;
        }

        consumer.Verify(c => c.Consume(It.IsAny<TimeSpan>()), Times.Exactly(expectedConsumes));
        consumerSource.Verify(s => s.GetConsumer(), Times.Once);
    }
}