using Confluent.Kafka;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Utility;

public class KafkaConsumerWrapperTests
{
    private static ConsumeResult<string, string> CreateResult(
        string topic,
        int partition,
        long offset,
        string? key,
        string? value)
    {
        return new ConsumeResult<string, string>
        {
            Topic = topic,
            Partition = new Partition(partition),
            Offset = new Offset(offset),
            Message = new Message<string, string>
            {
                Key = key!,
                Value = value!
            }
        };
    }

    [Fact]
    public void Consume_WhenMessagePresent_ReturnsContainerMappedFromResult()
    {
        var result = CreateResult("orders", 2, 15, "k", "payload");
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(TimeSpan.FromSeconds(1))).Returns(result);

        var wrapper = new KafkaConsumerWrapper(consumer.Object);
        var message = wrapper.Consume(TimeSpan.FromSeconds(1));

        Assert.NotNull(message);
        Assert.Equal("k", message.Key);
        Assert.Equal("payload", message.Value);
        Assert.Equal("orders", message.Topic);
        Assert.Equal(2, message.Partition);
        Assert.Equal(15, message.Offset);
        Assert.Equal("orders:2:15", message.MessageId);
        Assert.False(message.MessageIdIsDefault);
        consumer.Verify(c => c.Consume(TimeSpan.FromSeconds(1)), Times.Once);
    }

    [Fact]
    public void Consume_WhenResultIsNull_ReturnsNull()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(TimeSpan.FromMilliseconds(250)))
            .Returns((ConsumeResult<string, string>) null!);

        var wrapper = new KafkaConsumerWrapper(consumer.Object);

        Assert.Null(wrapper.Consume(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void Consume_WhenMessageIsNull_ReturnsNull()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Returns(new ConsumeResult<string, string>
        {
            Topic = "t",
            Partition = new Partition(0),
            Offset = new Offset(1),
            Message = null
        });

        var wrapper = new KafkaConsumerWrapper(consumer.Object);

        Assert.Null(wrapper.Consume(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Commit_WhenNoMessages_DoesNotCallConsumer()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        var wrapper = new KafkaConsumerWrapper(consumer.Object);

        wrapper.Commit([]);

        consumer.VerifyNoOtherCalls();
    }

    [Fact]
    public void Commit_CommitsNextOffsetsForEachMessage()
    {
        IReadOnlyList<TopicPartitionOffset>? committed = null;
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.Commit(It.IsAny<IEnumerable<TopicPartitionOffset>>()))
            .Callback<IEnumerable<TopicPartitionOffset>>(offsets => committed = offsets.ToList());

        var message1 = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        message1.SetupGet(m => m.Topic).Returns("t");
        message1.SetupGet(m => m.Partition).Returns(0);
        message1.SetupGet(m => m.Offset).Returns(10);

        var message2 = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        message2.SetupGet(m => m.Topic).Returns("t");
        message2.SetupGet(m => m.Partition).Returns(1);
        message2.SetupGet(m => m.Offset).Returns(20);

        var wrapper = new KafkaConsumerWrapper(consumer.Object);
        wrapper.Commit([message1.Object, message2.Object]);

        Assert.NotNull(committed);
        Assert.Equal(2, committed.Count);
        Assert.Equal(new TopicPartitionOffset("t", 0, new Offset(11)), committed[0]);
        Assert.Equal(new TopicPartitionOffset("t", 1, new Offset(21)), committed[1]);
    }

    [Fact]
    public void Dispose_ClosesAndDisposesUnderlyingConsumer()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Close());
        consumer.Setup(c => c.Dispose());

        var wrapper = new KafkaConsumerWrapper(consumer.Object);
        wrapper.Dispose();

        consumer.Verify(c => c.Close(), Times.Once);
        consumer.Verify(c => c.Dispose(), Times.Once);
    }
}
