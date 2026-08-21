using Apache.NMS;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqJobSourceTests
{
    private static (ActiveMqJobSource JobSource, Mock<IActiveMqConsumerRetryWrapper> ConsumerRetryWrapper)
        CreateJobSource(IMessageConsumer consumer, string? queueName = null)
    {
        var consumerRetryWrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        consumerRetryWrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IMessageConsumer, CancellationToken, Task> callback, Action<IConnection>? _,
                Action<IMessageConsumer>? __, CancellationToken token) => callback(consumer, token));

        var jobSource = new ActiveMqJobSource(
            ActiveMqRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            consumerRetryWrapper.Object,
            Options.Create(new ActiveMqConfigurationModel
            {
                QueueName = queueName!
            }),
            new NullLogger<ActiveMqJobSource>());

        return (jobSource, consumerRetryWrapper);
    }

    private static void VerifyWrapperCalled(Mock<IActiveMqConsumerRetryWrapper> consumerRetryWrapper, Times times)
    {
        consumerRetryWrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<Action<IMessageConsumer>?>(),
            TestContext.Current.CancellationToken), times);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        var message = new Mock<IMessage>();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var (jobSource, _) = CreateJobSource(consumer.Object);

        var jobModel = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(jobModel, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        message.Verify(m => m.AcknowledgeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    [InlineData(CoreJobResult.InvalidData)]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    public async Task Test_AcknowledgeAsync_AlwaysAcknowledges(CoreJobResult result)
    {
        var message = new Mock<IMessage>();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var (jobSource, _) = CreateJobSource(consumer.Object);

        var jobModel = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(jobModel, result,
            TestContext.Current.CancellationToken);

        message.Verify(m => m.AcknowledgeAsync(), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Incompatible()
    {
        var job = new Mock<IRawJobModel>();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var consumerRetryWrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);

        var jobSource = new ActiveMqJobSource(
            ActiveMqRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            consumerRetryWrapper.Object,
            Options.Create(new ActiveMqConfigurationModel
            {
                QueueName = null!
            }),
            new NullLogger<ActiveMqJobSource>());

        await jobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        consumerRetryWrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<Action<IMessageConsumer>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(consumer.Invocations);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var queueName = Guid.NewGuid().ToString();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => null);

        var (jobSource, consumerRetryWrapper) = CreateJobSource(consumer.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);
        VerifyWrapperCalled(consumerRetryWrapper, Times.Once());
        consumer.Verify(c => c.ReceiveAsync(TimeSpan.FromMilliseconds(100)), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GotJob()
    {
        var queueName = Guid.NewGuid().ToString();
        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<ITextMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);
        mockMessage.Setup(m => m.Text).Returns("{}");

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var (jobSource, consumerRetryWrapper) = CreateJobSource(consumer.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal("{}", returnedJobItem.Body);
        VerifyWrapperCalled(consumerRetryWrapper, Times.Once());
    }

    [Fact]
    public async Task Test_GetJobs_GotJob_BatchSizeZero()
    {
        var queueName = Guid.NewGuid().ToString();
        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<ITextMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);
        mockMessage.Setup(m => m.Text).Returns("{}");

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var (jobSource, consumerRetryWrapper) = CreateJobSource(consumer.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(0, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal("{}", returnedJobItem.Body);
        // batchSize is floored to 1, so a single successful receive ends the loop.
        VerifyWrapperCalled(consumerRetryWrapper, Times.Once());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task Test_GetJobs_GotJobs_MultipleJobs(int batchSize)
    {
        var queueName = Guid.NewGuid().ToString();
        var messageIds = new List<string>();
        var bodyStrings = new List<string>();
        var mockChannelQueue = new Queue<IMessage>();

        for (var i = 0; i < batchSize; i++)
        {
            var messageId = Guid.NewGuid().ToString();
            var body = i.ToString();
            messageIds.Add(messageId);
            bodyStrings.Add(body);
            var mockMessage = new Mock<ITextMessage>();
            mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);
            mockMessage.Setup(m => m.Text).Returns(body);
            mockChannelQueue.Enqueue(mockMessage.Object);
        }

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var (jobSource, consumerRetryWrapper) = CreateJobSource(consumer.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(batchSize, jobResponse.Items.Count);
        VerifyWrapperCalled(consumerRetryWrapper, Times.Exactly(batchSize));

        for (var i = 0; i < batchSize; i++)
        {
            Assert.Equal(messageIds[i], jobResponse.Items[i].MessageId);
            Assert.Equal(bodyStrings[i], jobResponse.Items[i].Body);
        }
    }

    [Fact]
    public async Task Test_GetJobs_PropagatesConsumerWrapperException()
    {
        var queueName = Guid.NewGuid().ToString();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var consumerRetryWrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        consumerRetryWrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var jobSource = new ActiveMqJobSource(
            ActiveMqRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            consumerRetryWrapper.Object,
            Options.Create(new ActiveMqConfigurationModel
            {
                QueueName = queueName
            }),
            new NullLogger<ActiveMqJobSource>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));

        Assert.Empty(consumer.Invocations);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var (jobSource, _) = CreateJobSource(consumer.Object);

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
    }
}