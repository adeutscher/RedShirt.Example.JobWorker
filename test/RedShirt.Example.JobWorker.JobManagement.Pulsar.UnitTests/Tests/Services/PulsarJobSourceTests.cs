using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

public class PulsarJobSourceTests
{
    private static IPulsarMessageContainer CreateMessage(string messageId, string? value)
    {
        var message = new Mock<IPulsarMessageContainer>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.Value).Returns(value);
        message.SetupGet(m => m.Topic).Returns("t");
        message.SetupGet(m => m.Partition).Returns(0);
        return message.Object;
    }

    private static IPulsarMessageSourceResponse CreateResponse(params IPulsarMessageContainer[] messages)
    {
        return new PulsarMessageSourceResponse
        {
            Messages = messages
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgeCompletionAsync_AcksOrNacksPerMessage_ClearsSessionAfterBatch(bool lastSuccess)
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var mock2 = new Mock<IJobDataModel>().Object;

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        consumer.Setup(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        await jobSource.AcknowledgeCompletionAsync(response.Items[0], true, TestContext.Current.CancellationToken);
        consumer.Verify(c => c.AcknowledgeAsync(message1, It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(jobSource.Session);

        await jobSource.AcknowledgeCompletionAsync(response.Items[1], lastSuccess,
            TestContext.Current.CancellationToken);

        if (lastSuccess)
        {
            consumer.Verify(c => c.AcknowledgeAsync(message2, It.IsAny<CancellationToken>()), Times.Once);
            consumer.Verify(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            consumer.Verify(c => c.NegativeAcknowledgeAsync(message2, It.IsAny<CancellationToken>()), Times.Once);
            consumer.Verify(c => c.AcknowledgeAsync(message2, It.IsAny<CancellationToken>()), Times.Never);
        }

        Assert.Null(jobSource.Session);
    }

    [Fact]
    public async Task AcknowledgeCompletionAsync_IgnoresNonPulsarModels()
    {
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        await jobSource.AcknowledgeCompletionAsync(new Mock<IJobModel>().Object, true,
            TestContext.Current.CancellationToken);

        Assert.Empty(consumerSource.Invocations);
        Assert.Empty(pulsarMessageSource.Invocations);
    }

    [Fact]
    public async Task AcknowledgeCompletionAsync_RoutesAckThroughRetryWrapper()
    {
        var data = Guid.NewGuid().ToString();
        var message = CreateMessage("t:0:1", data);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data)).Returns(new Mock<IJobDataModel>().Object);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        var retryInvoked = false;
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>(async (func, token) =>
            {
                retryInvoked = true;
                await func(token);
            });

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object, retry.Object,
            converter.Object, new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        await jobSource.AcknowledgeCompletionAsync(response.Items[0], true, TestContext.Current.CancellationToken);

        Assert.True(retryInvoked);
        consumer.Verify(c => c.AcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(jobSource.Session);
    }

    [Fact]
    public async Task AcknowledgeCompletionAsync_UnknownMessageId_DoesNotAckOrClearSession()
    {
        var data = Guid.NewGuid().ToString();
        var message = CreateMessage("t:0:1", data);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data)).Returns(new Mock<IJobDataModel>().Object);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        Assert.NotNull(jobSource.Session);

        var unknownPulsarJob = new PulsarJobModel
        {
            Message = CreateMessage("t:0:999", "x"),
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        await jobSource.AcknowledgeCompletionAsync(unknownPulsarJob, true, TestContext.Current.CancellationToken);

        Assert.NotNull(jobSource.Session);
        Assert.False(jobSource.Session.IsComplete);
        consumer.Verify(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcknowledgeCompletionAsync_WhenAckFails_PropagatesWorkerJobSourceException()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(new Mock<IJobDataModel>().Object);
        converter.Setup(c => c.Convert(data2)).Returns(new Mock<IJobDataModel>().Object);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.AcknowledgeAsync(message1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        // ReSharper disable once RedundantArgumentDefaultValue
        var failure = new WorkerJobSourceException("ack failed", true);
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        var attempt = 0;
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>(async (func, token) =>
            {
                attempt++;
                if (attempt == 1)
                {
                    await func(token);
                    return;
                }

                throw failure;
            });

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object, retry.Object,
            converter.Object, new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        await jobSource.AcknowledgeCompletionAsync(response.Items[0], true, TestContext.Current.CancellationToken);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            jobSource.AcknowledgeCompletionAsync(response.Items[1], true, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.NotNull(jobSource.Session);
        Assert.False(jobSource.Session.IsComplete);
    }

    [Fact]
    public async Task AcknowledgeCompletionAsync_WhenSessionIsNull_DoesNothing()
    {
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        Assert.Null(jobSource.Session);

        await jobSource.AcknowledgeCompletionAsync(new PulsarJobModel
        {
            Message = CreateMessage("t:0:1", "x"),
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        }, true, TestContext.Current.CancellationToken);

        Assert.Null(jobSource.Session);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_AllNullConverts_NegativelyAcknowledgesAndDoesNotOpenSession()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(It.IsAny<string>())).Returns((IJobDataModel?) null);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.NegativeAcknowledgeAsync(It.IsAny<IReadOnlyList<IPulsarMessageContainer>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.Is<IReadOnlyList<IPulsarMessageContainer>>(m =>
            m.Count == 2 && m.Contains(message1) && m.Contains(message2)), It.IsAny<CancellationToken>()), Times.Once);
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

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Same(mock2, response.Items[1].Data);
        Assert.Equal("t:0:1", response.Items[0].MessageId);
        Assert.Equal("t:0:2", response.Items[1].MessageId);
        Assert.NotNull(jobSource.Session);
        Assert.Equal(2, jobSource.Session.MessagesToProcess.Count);
        Assert.Same(message1, jobSource.Session.MessagesToProcess[0]);
        Assert.Same(message2, jobSource.Session.MessagesToProcess[1]);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_EmptySourceResponse_ReturnsNoItems()
    {
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
        converter.Verify(c => c.Convert(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_NegativelyAcknowledgesBadMessages_WhenSomeSucceed()
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

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2, message3, message4, emptyMessage));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data3)).Throws(new Exception("Controlled Test Blast"));
        converter.Setup(c => c.Convert(data4)).Returns(new Mock<IJobDataModel>().Object);

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.NegativeAcknowledgeAsync(It.IsAny<IReadOnlyList<IPulsarMessageContainer>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.NotNull(jobSource.Session);
        Assert.Equal(2, jobSource.Session.MessagesToProcess.Count);
        Assert.Same(message1, jobSource.Session.MessagesToProcess[0]);
        Assert.Same(message4, jobSource.Session.MessagesToProcess[1]);
        Assert.Equal(5, jobSource.Session.TotalMessages.Count);

        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.Is<IReadOnlyList<IPulsarMessageContainer>>(m =>
                m.Count == 3 && m.Contains(message2) && m.Contains(message3) && m.Contains(emptyMessage)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_NegativelyAcknowledgesSkippedMessages_WhenEveryMessageIsSkipped()
    {
        var data1 = Guid.NewGuid().ToString();
        var message1 = CreateMessage("t:0:1", "   ");
        var message2 = CreateMessage("t:0:2", data1);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Throws(new Exception("Controlled Test Blast"));

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.NegativeAcknowledgeAsync(It.IsAny<IReadOnlyList<IPulsarMessageContainer>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object, converter.Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.Is<IReadOnlyList<IPulsarMessageContainer>>(m =>
            m.Count == 2 && m.Contains(message1) && m.Contains(message2)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_WhenDeadLetterNackFails_PropagatesWorkerJobSourceException()
    {
        var message = CreateMessage("t:0:1", "   ");

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer())
            .Returns(new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict).Object);

        var failure = new WorkerJobSourceException("nack failed", false, false,
            true);
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object, retry.Object,
            converter.Object, new NullLogger<PulsarJobSource>());

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var jobSource = new PulsarJobSource(
            new Mock<IPulsarConsumerSource>().Object,
            new Mock<IPulsarMessageSource>().Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new Mock<ISourceMessageConverter>().Object,
            new NullLogger<PulsarJobSource>());

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        await jobSource.HeartbeatAsync(new Mock<IJobModel>().Object, TestContext.Current.CancellationToken);
    }
}