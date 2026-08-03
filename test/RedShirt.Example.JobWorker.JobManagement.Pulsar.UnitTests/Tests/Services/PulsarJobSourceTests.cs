using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;
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
    [InlineData(CoreJobResult.Success, true)]
    [InlineData(CoreJobResult.Failure, false)]
    [InlineData(CoreJobResult.Cancelled, false)]
    [InlineData(CoreJobResult.Empty, false)]
    [InlineData(CoreJobResult.Parsing, false)]
    [InlineData(CoreJobResult.Broken, false)]
    public async Task AcknowledgeAsync_AcksOrNacksPerMessage_ClearsSessionAfterBatch(
        CoreJobResult lastResult, bool expectAck)
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        consumer.Setup(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        await jobSource.AcknowledgeAsync(response.Items[0], CoreJobResult.Success,
            TestContext.Current.CancellationToken);
        consumer.Verify(c => c.AcknowledgeAsync(message1, It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(jobSource.Session);

        await jobSource.AcknowledgeAsync(response.Items[1], lastResult, TestContext.Current.CancellationToken);

        if (expectAck)
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
    public async Task AcknowledgeAsync_IgnoresNonPulsarModels()
    {
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.Empty(consumerSource.Invocations);
        Assert.Empty(pulsarMessageSource.Invocations);
    }

    [Fact]
    public async Task AcknowledgeAsync_RoutesAckThroughRetryWrapper()
    {
        var data = Guid.NewGuid().ToString();
        var message = CreateMessage("t:0:1", data);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

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
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        await jobSource.AcknowledgeAsync(response.Items[0], CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.True(retryInvoked);
        consumer.Verify(c => c.AcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(jobSource.Session);
    }

    [Fact]
    public async Task AcknowledgeAsync_UnknownMessageId_DoesNotAckOrClearSession()
    {
        var data = Guid.NewGuid().ToString();
        var message = CreateMessage("t:0:1", data);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumer()).Returns(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        Assert.NotNull(jobSource.Session);

        var unknownPulsarJob = new PulsarJobModel
        {
            Message = CreateMessage("t:0:999", "x"),
            CreatedAtUtc = DateTime.UtcNow,
            Body = "x"
        };

        await jobSource.AcknowledgeAsync(unknownPulsarJob, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobSource.Session);
        Assert.False(jobSource.Session.IsComplete);
        consumer.Verify(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        consumer.Verify(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenAckFails_PropagatesWorkerJobSourceException()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

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
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);
        await jobSource.AcknowledgeAsync(response.Items[0], CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            jobSource.AcknowledgeAsync(response.Items[1], CoreJobResult.Success,
                TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.NotNull(jobSource.Session);
        Assert.False(jobSource.Session.IsComplete);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenSessionIsNull_DoesNothing()
    {
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        Assert.Null(jobSource.Session);

        await jobSource.AcknowledgeAsync(new PulsarJobModel
        {
            Message = CreateMessage("t:0:1", "x"),
            CreatedAtUtc = DateTime.UtcNow,
            Body = "x"
        }, CoreJobResult.Success, TestContext.Current.CancellationToken);

        Assert.Null(jobSource.Session);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_MapsMessagesToRawJobModels_AndOpensSession()
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();

        var message1 = CreateMessage("t:0:1", data1);
        var message2 = CreateMessage("t:0:2", data2);

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, message2));

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

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
    public async Task GetJobsAsync_EmptySourceResponse_ReturnsNoItems()
    {
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Null(jobSource.Session);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_IncludesEmptyBodies_ForCoreIntake()
    {
        var message1 = CreateMessage("t:0:1", """{"ok":true}""");
        var emptyMessage = CreateMessage("t:0:2", "   ");

        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1, emptyMessage));

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("   ", response.Items[1].Body);
        Assert.NotNull(jobSource.Session);
        Assert.Equal(2, jobSource.Session.MessagesToProcess.Count);
        consumerSource.Verify(s => s.GetConsumer(), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_WhenSessionOpen_ReturnsEmptyWithoutPolling()
    {
        var message = CreateMessage("t:0:1", "body");
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.Setup(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message));

        var jobSource = new PulsarJobSource(
            new Mock<IPulsarConsumerSource>(MockBehavior.Strict).Object,
            pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        var second = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Empty(second.Items);
        pulsarMessageSource.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var jobSource = new PulsarJobSource(
            new Mock<IPulsarConsumerSource>().Object,
            new Mock<IPulsarMessageSource>().Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>());

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);
    }
}
