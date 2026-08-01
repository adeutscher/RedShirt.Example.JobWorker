using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
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
            Messages = messages
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgeAsync_CommitsMessagesOnlyAfterEntireBatch(bool lastSuccess)
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        await jobSource.AcknowledgeAsync(response.Items[0], true, TestContext.Current.CancellationToken);
        consumer.Verify(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.NotNull(jobSource.Session);

        await jobSource.AcknowledgeAsync(response.Items[1], lastSuccess,
            TestContext.Current.CancellationToken);
        consumer.Verify(c => c.CommitAsync(It.Is<List<IKafkaMessageContainer>>(m =>
            m.Count == 2 && m.Contains(message1) && m.Contains(message2)), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(jobSource.Session);
    }

    [Fact]
    public async Task AcknowledgeAsync_IgnoresNonKafkaModels()
    {
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, true,
            TestContext.Current.CancellationToken);

        Assert.Empty(consumerSource.Invocations);
        Assert.Empty(kafkaMessageSource.Invocations);
    }

    [Fact]
    public async Task AcknowledgeAsync_RoutesCommitThroughRetryWrapper()
    {
        var data = Guid.NewGuid().ToString();
        var message = CreateMessage("t:0:1", data);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        var retryInvoked = false;
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>(async (func, token) =>
            {
                retryInvoked = true;
                await func(token);
            });

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, retry.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        await jobSource.AcknowledgeAsync(response.Items[0], true, TestContext.Current.CancellationToken);

        Assert.True(retryInvoked);
        consumer.Verify(c => c.CommitAsync(It.Is<List<IKafkaMessageContainer>>(m => m.Single() == message),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Null(jobSource.Session);
    }

    [Fact]
    public async Task AcknowledgeAsync_UnknownMessageId_DoesNotCommitOrClearSession()
    {
        var data = Guid.NewGuid().ToString();
        var message = CreateMessage("t:0:1", data);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        Assert.NotNull(jobSource.Session);

        var unknownKafkaJob = new KafkaJobModel
        {
            Message = CreateMessage("t:0:999", "x"),
            CreatedAtUtc = DateTime.UtcNow,
            Body = "x"
        };

        await jobSource.AcknowledgeAsync(unknownKafkaJob, true, TestContext.Current.CancellationToken);

        Assert.NotNull(jobSource.Session);
        Assert.False(jobSource.Session.IsComplete);
        consumer.Verify(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenCommitFails_PropagatesWorkerJobSourceException()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict).Object);

        // ReSharper disable once RedundantArgumentDefaultValue
        var failure = new WorkerJobSourceException("commit failed", true);
        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, retry.Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        await jobSource.AcknowledgeAsync(response.Items[0], true, TestContext.Current.CancellationToken);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            jobSource.AcknowledgeAsync(response.Items[1], true, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.NotNull(jobSource.Session);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenSessionIsNull_DoesNothing()
    {
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        Assert.Null(jobSource.Session);

        await jobSource.AcknowledgeAsync(new KafkaJobModel
        {
            Message = CreateMessage("t:0:1", "x"),
            CreatedAtUtc = DateTime.UtcNow,
            Body = "x"
        }, true, TestContext.Current.CancellationToken);

        Assert.Null(jobSource.Session);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_AllEmptyBodies_CommitsSkippedAndReturnsEmpty()
    {
        var message1 = CreateMessage("t:0:1", "   ");
        var message2 = CreateMessage("t:0:2", "\t");

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumer.Verify(c => c.CommitAsync(It.Is<List<IKafkaMessageContainer>>(m =>
            m.Count == 2 && m.Contains(message1) && m.Contains(message2)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_CommitsSkippedMessages_WhenEveryMessageIsSkipped()
    {
        var message1 = CreateMessage("t:0:1", "   ");
        var message2 = CreateMessage("t:0:2", "");

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumer.Verify(c => c.CommitAsync(It.Is<List<IKafkaMessageContainer>>(m =>
            m.Count == 2 && m.Contains(message1) && m.Contains(message2)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_EmptySourceResponse_ReturnsNoItems()
    {
        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_ReturnsRawBodies_AndOpensSession()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal("t:0:1", response.Items[0].MessageId);
        Assert.Equal("t:0:2", response.Items[1].MessageId);
        Assert.NotNull(jobSource.Session);
        Assert.Equal(2, jobSource.Session.MessagesToProcess.Count);
        Assert.Same(message1, jobSource.Session.MessagesToProcess[0]);
        Assert.Same(message2, jobSource.Session.MessagesToProcess[1]);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_SkipsEmptyBodies_WithoutImmediateCommit_WhenSomeSucceed()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);
        var message3 = CreateMessage("t:0:3", data3);
        var message4 = CreateMessage("t:0:4", data4);
        var emptyMessage = CreateMessage("t:0:5", "   ");

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2, message3, message4, emptyMessage));

        var consumer = new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Equal(4, response.Items.Count);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(data4, response.Items[3].Body);
        Assert.NotNull(jobSource.Session);
        Assert.Equal(4, jobSource.Session.MessagesToProcess.Count);
        Assert.Same(message1, jobSource.Session.MessagesToProcess[0]);
        Assert.Same(message4, jobSource.Session.MessagesToProcess[3]);
        Assert.Equal(5, jobSource.Session.TotalMessages.Count);

        consumer.Verify(c => c.CommitAsync(It.IsAny<List<IKafkaMessageContainer>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_WhenAllSkippedCommitFails_PropagatesWorkerJobSourceException()
    {
        var message = CreateMessage("t:0:1", "   ");

        var kafkaMessageSource = new Mock<IKafkaMessageSource>(MockBehavior.Strict);
        kafkaMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var consumerSource = new Mock<IKafkaConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(new Mock<IKafkaConsumerWrapper>(MockBehavior.Strict).Object);

        var failure = new WorkerJobSourceException("commit failed", false, false,
            true);
        var retry = new Mock<IKafkaRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var jobSource = new KafkaJobSource(consumerSource.Object, kafkaMessageSource.Object, retry.Object,
            new NullLogger<KafkaJobSource>());

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var jobSource = new KafkaJobSource(
            new Mock<IKafkaConsumerSource>().Object,
            new Mock<IKafkaMessageSource>().Object,
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<KafkaJobSource>());

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);
    }
}