using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private static IOptions<PulsarConsumerFactory.ConfigurationModel> CreateOptions(
        string subscriptionName = "test-subscription")
    {
        return Options.Create(new PulsarConsumerFactory.ConfigurationModel
        {
            ServiceUrl = "pulsar://localhost:6650",
            SubscriptionName = subscriptionName,
            Topic = "persistent://public/default/test-topic"
        });
    }

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
    public async Task AcknowledgeAsync_AcksOrNacksIndependently(CoreJobResult result, bool expectAck)
    {
        var message = CreateMessage("t:0:1", Guid.NewGuid().ToString());

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        consumer.Setup(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var jobSource = new PulsarJobSource(consumerSource.Object,
            new Mock<IPulsarMessageSource>(MockBehavior.Strict).Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        await jobSource.AcknowledgeAsync(new PulsarJobModel
        {
            Message = message,
            CreatedAtUtc = DateTime.UtcNow,
            Body = message.Value
        }, result, TestContext.Current.CancellationToken);

        if (expectAck)
        {
            consumer.Verify(c => c.AcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
            consumer.Verify(c => c.NegativeAcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            consumer.Verify(c => c.NegativeAcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
            consumer.Verify(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    public async Task AcknowledgeAsync_IgnoresNonPulsarModels()
    {
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);

        var jobSource = new PulsarJobSource(consumerSource.Object, pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.Empty(consumerSource.Invocations);
        Assert.Empty(pulsarMessageSource.Invocations);
    }

    [Fact]
    public async Task AcknowledgeAsync_RoutesAckThroughRetryWrapper()
    {
        var message = CreateMessage("t:0:1", Guid.NewGuid().ToString());

        var consumer = new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict);
        consumer.Setup(c => c.AcknowledgeAsync(It.IsAny<IPulsarMessageContainer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(consumer.Object);

        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        var retryInvoked = false;
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>(async (func, token) =>
            {
                retryInvoked = true;
                await func(token);
            });

        var jobSource = new PulsarJobSource(consumerSource.Object,
            new Mock<IPulsarMessageSource>(MockBehavior.Strict).Object, retry.Object,
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        await jobSource.AcknowledgeAsync(new PulsarJobModel
        {
            Message = message,
            CreatedAtUtc = DateTime.UtcNow,
            Body = message.Value
        }, CoreJobResult.Success, TestContext.Current.CancellationToken);

        Assert.True(retryInvoked);
        consumer.Verify(c => c.AcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenAckFails_PropagatesWorkerJobSourceException()
    {
        var message = CreateMessage("t:0:1", Guid.NewGuid().ToString());

        var consumerSource = new Mock<IPulsarConsumerSource>(MockBehavior.Strict);
        consumerSource.Setup(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<IPulsarConsumerWrapper>(MockBehavior.Strict).Object);

        // ReSharper disable once RedundantArgumentDefaultValue
        var failure = new WorkerJobSourceException("ack failed", true);
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var jobSource = new PulsarJobSource(consumerSource.Object,
            new Mock<IPulsarMessageSource>(MockBehavior.Strict).Object, retry.Object,
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            jobSource.AcknowledgeAsync(new PulsarJobModel
            {
                Message = message,
                CreatedAtUtc = DateTime.UtcNow,
                Body = message.Value
            }, CoreJobResult.Success, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task GetJobsAsync_AllowsPollingWhilePriorMessagesAreInFlight()
    {
        var message1 = CreateMessage("t:0:1", "body-1");
        var message2 = CreateMessage("t:0:2", "body-2");
        var pulsarMessageSource = new Mock<IPulsarMessageSource>(MockBehavior.Strict);
        pulsarMessageSource.SetupSequence(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(message1))
            .ReturnsAsync(CreateResponse(message2));

        var jobSource = new PulsarJobSource(
            new Mock<IPulsarConsumerSource>(MockBehavior.Strict).Object,
            pulsarMessageSource.Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        var first = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);
        var second = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal("body-2", second.Items[0].Body);
        pulsarMessageSource.Verify(a => a.GetMessagesAsync(1, It.IsAny<CancellationToken>()), Times.Exactly(2));
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
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        consumerSource.Verify(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()), Times.Never);
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
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("   ", response.Items[1].Body);
        consumerSource.Verify(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_MapsMessagesToRawJobModels()
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
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal("t:0:1", response.Items[0].MessageId);
        Assert.Equal("t:0:2", response.Items[1].MessageId);
        consumerSource.Verify(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var jobSource = new PulsarJobSource(
            new Mock<IPulsarConsumerSource>().Object,
            new Mock<IPulsarMessageSource>().Object,
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<PulsarJobSource>(),
            CreateOptions());

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);
    }
}