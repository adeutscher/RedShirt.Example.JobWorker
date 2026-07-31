using Confluent.Kafka;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;
using RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;
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

    private static KafkaConsumerWrapper CreateWrapper(
        IConsumer<string, string> consumer,
        IKafkaRetryWrapperService? retryWrapper = null)
    {
        return new KafkaConsumerWrapper(
            retryWrapper ?? KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            consumer);
    }

    [Fact]
    public async Task CommitAsync_CommitsNextOffsetForEachMessageIndividually()
    {
        var committed = new List<TopicPartitionOffset>();
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.Commit(It.IsAny<IEnumerable<TopicPartitionOffset>>()))
            .Callback<IEnumerable<TopicPartitionOffset>>(offsets =>
                committed.AddRange(offsets as List<TopicPartitionOffset> ?? offsets.ToList()));

        var message1 = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        message1.SetupGet(m => m.Topic).Returns("t");
        message1.SetupGet(m => m.Partition).Returns(0);
        message1.SetupGet(m => m.Offset).Returns(10);

        var message2 = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        message2.SetupGet(m => m.Topic).Returns("t");
        message2.SetupGet(m => m.Partition).Returns(1);
        message2.SetupGet(m => m.Offset).Returns(20);

        var wrapper = CreateWrapper(consumer.Object);
        await wrapper.CommitAsync([message1.Object, message2.Object], TestContext.Current.CancellationToken);

        Assert.Equal(2, committed.Count);
        Assert.Equal(new TopicPartitionOffset("t", 0, new Offset(11)), committed[0]);
        Assert.Equal(new TopicPartitionOffset("t", 1, new Offset(21)), committed[1]);
        consumer.Verify(c => c.Commit(It.IsAny<IEnumerable<TopicPartitionOffset>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CommitAsync_RoutesEachOffsetThroughRetryWrapper()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Commit(It.IsAny<IEnumerable<TopicPartitionOffset>>()));

        var retryCalls = 0;
        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) =>
            {
                retryCalls++;
                return func(token);
            });

        var message = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        message.SetupGet(m => m.Topic).Returns("t");
        message.SetupGet(m => m.Partition).Returns(0);
        message.SetupGet(m => m.Offset).Returns(5);

        var wrapper = CreateWrapper(consumer.Object, retry.Object);
        await wrapper.CommitAsync([message.Object], TestContext.Current.CancellationToken);

        Assert.Equal(1, retryCalls);
        consumer.Verify(c => c.Commit(It.IsAny<IEnumerable<TopicPartitionOffset>>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WhenNoMessages_DoesNotCallConsumerOrRetry()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        var wrapper = CreateWrapper(consumer.Object, retry.Object);

        await wrapper.CommitAsync([], TestContext.Current.CancellationToken);

        consumer.VerifyNoOtherCalls();
        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommitAsync_WhenPermanentNonCriticalFailure_SkipsRemainingOffsetsOnSamePartition()
    {
        var committedPartitions = new List<int>();
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.Commit(It.IsAny<IEnumerable<TopicPartitionOffset>>()))
            .Callback<IEnumerable<TopicPartitionOffset>>(offsets =>
            {
                var offset = offsets.Single();
                committedPartitions.Add(offset.Partition);
            });

        var permanent = new WorkerJobSourceException("lost ownership", false, false,
            true);
        var retryAttempts = 0;
        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) =>
            {
                retryAttempts++;
                if (retryAttempts == 1)
                {
                    return Task.FromException(permanent);
                }

                return func(token);
            });

        var partition0First = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        partition0First.SetupGet(m => m.Topic).Returns("t");
        partition0First.SetupGet(m => m.Partition).Returns(0);
        partition0First.SetupGet(m => m.Offset).Returns(1);

        var partition0Second = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        partition0Second.SetupGet(m => m.Topic).Returns("t");
        partition0Second.SetupGet(m => m.Partition).Returns(0);
        partition0Second.SetupGet(m => m.Offset).Returns(2);

        var partition1 = new Mock<IKafkaMessageContainer>(MockBehavior.Strict);
        partition1.SetupGet(m => m.Topic).Returns("t");
        partition1.SetupGet(m => m.Partition).Returns(1);
        partition1.SetupGet(m => m.Offset).Returns(3);

        var wrapper = CreateWrapper(consumer.Object, retry.Object);
        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.CommitAsync(
            [partition0First.Object, partition0Second.Object, partition1.Object],
            TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        Assert.Equal([1], committedPartitions);
        Assert.Equal(2, retryAttempts);
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

        var wrapper = CreateWrapper(consumer.Object);

        Assert.Null(wrapper.Consume(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Consume_WhenMessagePresent_ReturnsContainerMappedFromResult()
    {
        var result = CreateResult("orders", 2, 15, "k", "payload");
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Consume(TimeSpan.FromSeconds(1))).Returns(result);

        var wrapper = CreateWrapper(consumer.Object);
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

        var wrapper = CreateWrapper(consumer.Object);

        Assert.Null(wrapper.Consume(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void Dispose_ClosesAndDisposesUnderlyingConsumer()
    {
        var consumer = new Mock<IConsumer<string, string>>(MockBehavior.Strict);
        consumer.Setup(c => c.Close());
        consumer.Setup(c => c.Dispose());

        var wrapper = CreateWrapper(consumer.Object);
        wrapper.Dispose();
        wrapper.Dispose();

        consumer.Verify(c => c.Close(), Times.Once);
        consumer.Verify(c => c.Dispose(), Times.Once);
    }
}