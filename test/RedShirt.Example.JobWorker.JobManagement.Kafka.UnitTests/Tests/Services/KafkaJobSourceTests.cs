using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;

public class KafkaJobSourceTests
{
    private static IKafkaMessageContainer CreateMessage(string messageId, string? value)
    {
        var message = new Mock<IKafkaMessageContainer>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.Value).Returns(value);
        message.SetupGet(m => m.Topic).Returns("t");
        message.SetupGet(m => m.Partition).Returns(0);
        message.SetupGet(m => m.Offset).Returns(long.Parse(messageId.Split(':')[^1]));
        return message.Object;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgeCompletionAsync_CommitsOnlyAfterEntireBatch(bool lastSuccess)
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var mock2 = new Mock<IJobDataModel>().Object;

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message1, message2]);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Commit(It.IsAny<IEnumerable<IKafkaMessageContainer>>()));

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        await jobSource.AcknowledgeCompletionAsync(response.Items[0], true, TestContext.Current.CancellationToken);
        consumer.Verify(c => c.Commit(It.IsAny<IEnumerable<IKafkaMessageContainer>>()), Times.Never);
        Assert.Single(jobSource.Sessions);

        await jobSource.AcknowledgeCompletionAsync(response.Items[1], lastSuccess,
            TestContext.Current.CancellationToken);
        consumer.Verify(c => c.Commit(It.Is<IEnumerable<IKafkaMessageContainer>>(m =>
            m.Count() == 2 && m.Contains(message1) && m.Contains(message2))), Times.Once);
        Assert.Empty(jobSource.Sessions);
    }

    [Fact]
    public async Task AcknowledgeCompletionAsync_IgnoresNonKafkaModels()
    {
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        await jobSource.AcknowledgeCompletionAsync(new Mock<IJobModel>().Object, true,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var jobSource = new KafkaJobSource(
            new Mock<IKafkaConsumerSource>().Object,
            new Mock<IKafkaMessageSource>().Object,
            new Mock<ISourceMessageConverter>().Object,
            new NullLogger<KafkaJobSource>());

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        await jobSource.HeartbeatAsync(new Mock<IJobModel>().Object, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TestGetJobsAsync_ConvertsAndSkipsFailures()
    {
        var data1 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);
        var message3 = CreateMessage("t:0:3", data3);
        var message4 = CreateMessage("t:0:4", data4);
        var emptyMessage = CreateMessage("t:0:5", "   ");

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message1, message2, message3, message4, emptyMessage]);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);
        converter.Setup(c => c.Convert(data3)).Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data4)).Throws(new Exception("Controlled Test Blast"));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Commit(It.IsAny<IEnumerable<IKafkaMessageContainer>>()));

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Same(mock2, response.Items[1].Data);
        Assert.Single(jobSource.Sessions);

        consumer.Verify(c => c.Commit(It.Is<IEnumerable<IKafkaMessageContainer>>(m =>
            m.Count() == 3 && m.Contains(message3) && m.Contains(message4) && m.Contains(emptyMessage))), Times.Once);
    }
}