using Apache.NMS;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqJobSourceTests
{
    private static ActiveMqJobSource CreateJobSource(
        IActiveMqConnectionFactory? factory,
        ActiveMqJobSource.ConfigurationModel configuration)
    {
        return new ActiveMqJobSource(
            factory!,
            ActiveMqRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(configuration),
            new NullLogger<ActiveMqJobSource>());
    }

    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        var message = new Mock<IMessage>();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null!
        };

        var activeMqJobSource = CreateJobSource(null, configuration);

        var jobModel = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow
        };

        await activeMqJobSource.AcknowledgeAsync(jobModel, CoreJobResult.Success,
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

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null!
        };

        var activeMqJobSource = CreateJobSource(null, configuration);

        var jobModel = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow
        };

        await activeMqJobSource.AcknowledgeAsync(jobModel, result,
            TestContext.Current.CancellationToken);

        message.Verify(m => m.AcknowledgeAsync(), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Incompatible()
    {
        var job = new Mock<IRawJobModel>();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null!
        };

        var activeMqJobSource = CreateJobSource(null, configuration);

        await activeMqJobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);

        var queue = new Mock<IQueue>(MockBehavior.Strict);

        var mockSession = new Mock<ISession>(MockBehavior.Strict);
        mockSession.Setup(s => s.GetQueueAsync(queueName))
            .ReturnsAsync(queue.Object);
        mockSession.Setup(s => s.CreateConsumerAsync(queue.Object))
            .ReturnsAsync(consumer.Object);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.StartAsync())
            .Returns(Task.CompletedTask);
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => null);

        var jobSource = CreateJobSource(activeConnectionFactory.Object, configuration);

        var jobResponse = await jobSource.GetJobsAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.StartAsync(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoQueue()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockSession = new Mock<ISession>(MockBehavior.Strict);
        mockSession.Setup(s => s.GetQueueAsync(queueName))
            .ReturnsAsync((IQueue?) null);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.StartAsync())
            .Returns(Task.CompletedTask);
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var jobSource = CreateJobSource(activeConnectionFactory.Object, configuration);

        await Assert.ThrowsAsync<CouldNotLoadQueueException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.StartAsync(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Single(mockSession.Invocations);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GotJob()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);

        var queue = new Mock<IQueue>(MockBehavior.Strict);

        var mockSession = new Mock<ISession>(MockBehavior.Strict);
        mockSession.Setup(s => s.GetQueueAsync(queueName))
            .ReturnsAsync(queue.Object);
        mockSession.Setup(s => s.CreateConsumerAsync(queue.Object))
            .ReturnsAsync(consumer.Object);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.StartAsync())
            .Returns(Task.CompletedTask);
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<ITextMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);
        mockMessage.Setup(m => m.Text).Returns("{}");

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var jobSource = CreateJobSource(activeConnectionFactory.Object, configuration);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal("{}", returnedJobItem.Body);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.StartAsync(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GotJob_BatchSizeZero()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);

        var queue = new Mock<IQueue>(MockBehavior.Strict);

        var mockSession = new Mock<ISession>(MockBehavior.Strict);
        mockSession.Setup(s => s.GetQueueAsync(queueName))
            .ReturnsAsync(queue.Object);
        mockSession.Setup(s => s.CreateConsumerAsync(queue.Object))
            .ReturnsAsync(consumer.Object);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.StartAsync())
            .Returns(Task.CompletedTask);
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<ITextMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);
        mockMessage.Setup(m => m.Text).Returns("{}");

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var jobSource = CreateJobSource(activeConnectionFactory.Object, configuration);

        var jobResponse = await jobSource.GetJobsAsync(0, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal("{}", returnedJobItem.Body);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task Test_GetJobs_GotJobs_MultipleJobs(int batchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);

        var queue = new Mock<IQueue>(MockBehavior.Strict);

        var mockSession = new Mock<ISession>(MockBehavior.Strict);
        mockSession.Setup(s => s.GetQueueAsync(queueName))
            .ReturnsAsync(queue.Object);
        mockSession.Setup(s => s.CreateConsumerAsync(queue.Object))
            .ReturnsAsync(consumer.Object);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.StartAsync())
            .Returns(Task.CompletedTask);
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var messageIds = new List<string>();
        var mockMessages = new List<Mock<ITextMessage>>();
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

            mockMessages.Add(mockMessage);
            mockChannelQueue.Enqueue(mockMessage.Object);
        }

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var jobSource = CreateJobSource(activeConnectionFactory.Object, configuration);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(batchSize, jobResponse.Items.Count);

        for (var i = 0; i < batchSize; i++)
        {
            var messageId = messageIds[i];
            var body = bodyStrings[i];

            var returnedJobItem = jobResponse.Items[i];
            Assert.Equal(messageId, returnedJobItem.MessageId);
            Assert.Equal(body, returnedJobItem.Body);
        }
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null!
        };

        var jobSource = CreateJobSource(null, configuration);

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
    }
}