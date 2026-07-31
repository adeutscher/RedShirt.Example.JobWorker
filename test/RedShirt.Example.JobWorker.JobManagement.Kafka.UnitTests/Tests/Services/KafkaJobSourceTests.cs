using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
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

    private static IKafkaMessageSourceResponse CreateResponse(params IKafkaMessageContainer[] messages)
    {
        return new KafkaMessageSourceResponse
        {
            Messages = messages,
            LastMessage = messages.Length > 0 ? messages[^1] : null
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgeCompletionAsync_CommitsMessagesOnlyAfterEntireBatch(bool lastSuccess)
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var mock2 = new Mock<IJobDataModel>().Object;

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

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
    public async Task GetJobsAsync_CommitsSkippedMessages_WhenEveryMessageIsSkipped()
    {
        var data1 = Guid.NewGuid().ToString();
        var message1 = CreateMessage("t:0:1", "   ");
        var message2 = CreateMessage("t:0:2", data1);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Throws(new Exception("Controlled Test Blast"));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.Commit(It.IsAny<IEnumerable<IKafkaMessageContainer>>()));

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Empty(jobSource.Sessions);
        consumer.Verify(c => c.Commit(It.Is<IEnumerable<IKafkaMessageContainer>>(m =>
            m.Count() == 2 && m.Contains(message1) && m.Contains(message2))), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_ConvertsValidMessages_AndOpensSession()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var mock2 = new Mock<IJobDataModel>().Object;

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Same(mock2, response.Items[1].Data);
        Assert.Equal("t:0:1", response.Items[0].MessageId);
        Assert.Equal("t:0:2", response.Items[1].MessageId);
        Assert.Single(jobSource.Sessions);
        Assert.Same(message2, jobSource.Sessions[0].LastMessage);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_EmptySourceResponse_ReturnsNoItems()
    {
        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Empty(jobSource.Sessions);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
        converter.Verify(c => c.Convert(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_SkipsBadMessages_WithoutImmediateCommit_WhenSomeSucceed()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);
        var message3 = CreateMessage("t:0:3", data3);
        var message4 = CreateMessage("t:0:4", data4);
        var emptyMessage = CreateMessage("t:0:5", "   ");

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2, message3, message4, emptyMessage));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data3)).Throws(new Exception("Controlled Test Blast"));
        // data4 unused because empty body is skipped before convert; message4 is still converted if value is non-empty
        converter.Setup(c => c.Convert(data4)).Returns(new Mock<IJobDataModel>().Object);

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, converter.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        // message1 converted; message2 null convert ignored; message3 exception skipped;
        // message4 converted; emptyMessage skipped for empty body
        Assert.Equal(2, response.Items.Count);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Single(jobSource.Sessions);
        Assert.Same(emptyMessage, jobSource.Sessions[0].LastMessage);

        // Mixed success/failure does not commit during GetJobsAsync
        consumer.Verify(c => c.Commit(It.IsAny<IEnumerable<IKafkaMessageContainer>>()), Times.Never);
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
}