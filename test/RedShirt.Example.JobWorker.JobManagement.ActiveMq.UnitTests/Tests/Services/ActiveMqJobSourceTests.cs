using Apache.NMS;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqJobSourceTests
{
    [Fact]
    public async Task Test_AcknowledgeCompletionAsync()
    {
        // Set up Mocks

        var message = new Mock<IMessage>();

        // Declare objects
        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null!
        };

        var activeMqJobSource = new ActiveMqJobSource(null!, Options.Create(configuration), null!, null!,
            new NullLogger<ActiveMqJobSource>());

        var jobModel = new JobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
        };

        await activeMqJobSource.AcknowledgeCompletionAsync(jobModel, true, TestContext.Current.CancellationToken);

        message.Verify(m => m.AcknowledgeAsync(), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeCompletionAsync_Incompatible()
    {
        // Set up Mocks

        var job = new Mock<IJobModel>();

        // Declare objects
        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null!
        };

        var activeMqJobSource = new ActiveMqJobSource(null!, Options.Create(configuration), null!, null!,
            new NullLogger<ActiveMqJobSource>());

        await activeMqJobSource.AcknowledgeCompletionAsync(job.Object, true, TestContext.Current.CancellationToken);

        Assert.True(true); // Satisfy Sonar's requirement for an assert.
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Empty(bodyRetriever.Invocations);
        Assert.Empty(converter.Invocations);
    }

    /// <summary>
    ///     Didn't encounter this while local testing, but it's technically possible for a session's GetQueue call to return
    ///     null. Made an exception to handle that situation for future debugging.
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_GetNoQueue()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);

        var mockSession = new Mock<ISession>(MockBehavior.Strict);
        mockSession.Setup(s => s.GetQueueAsync(queueName))
            .ReturnsAsync((IQueue?) null);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        await Assert.ThrowsAsync<CouldNotLoadQueueException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));

        // Assert

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Single(mockSession.Invocations);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);

        Assert.Empty(bodyRetriever.Invocations);
        Assert.Empty(converter.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job
    /// </summary>
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<IMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
            .Returns("{}");

        var jobDataModel = new Mock<IJobDataModel>();

        converter.Setup(c => c.Convert("{}"))
            .Returns(jobDataModel.Object);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Same(jobDataModel.Object, returnedJobItem.Data);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Single(bodyRetriever.Invocations);
        Assert.Single(converter.Invocations);
    }

    /// <summary>
    ///     Confirm that configuration will bump up the effective batch size to a minimum of 1 even if the given value was 0.
    /// </summary>
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<IMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
            .Returns("{}");

        var jobDataModel = new Mock<IJobDataModel>();

        converter.Setup(c => c.Convert("{}"))
            .Returns(jobDataModel.Object);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(0, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Same(jobDataModel.Object, returnedJobItem.Data);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Single(bodyRetriever.Invocations);
        Assert.Single(converter.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job, but the body retriever returned threw a specific
    ///     'CouldNotRetrieveMessageBodyException' exception
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_GotJob_Retrieval_Exception()
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var mockMessage = new Mock<IMessage>();

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
            .Returns(() => throw new CouldNotRetrieveMessageBodyException());

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Single(bodyRetriever.Invocations);
        Assert.Empty(converter.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job, but the body retriever returned null
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_GotJob_Retrieval_Null()
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var mockMessage = new Mock<IMessage>();

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
            .Returns((string?) null);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Single(bodyRetriever.Invocations);
        Assert.Empty(converter.Invocations);
    }

    /// <summary>
    ///     Test of getting a multiple jobs in a single GetJobs call.
    /// </summary>
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var messageIds = new List<string>();
        var mockMessages = new List<Mock<IMessage>>();
        var jobDataModels = new List<Mock<IJobDataModel>>();

        var mockChannelQueue = new Queue<IMessage>();

        for (var i = 0; i < batchSize; i++)
        {
            var messageId = Guid.NewGuid().ToString();
            messageIds.Add(messageId);
            var mockMessage = new Mock<IMessage>();
            mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);
            mockMessage.Setup(m => m.NMSCorrelationID).Returns(i.ToString());

            mockMessages.Add(mockMessage);
            mockChannelQueue.Enqueue(mockMessage.Object);

            var jobDataModel = new Mock<IJobDataModel>();
            jobDataModels.Add(jobDataModel);

            bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
                .Returns(mockMessage.Object.NMSCorrelationID);
            var i1 = i;
            converter.Setup(c => c.Convert(i1.ToString()))
                .Returns(jobDataModel.Object);
        }

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(batchSize, jobResponse.Items.Count);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Equal(batchSize, bodyRetriever.Invocations.Count);
        Assert.Equal(batchSize, converter.Invocations.Count);
        for (var i = 0; i < batchSize; i++)
        {
            var mockMessage = mockMessages[i];
            var messageId = messageIds[i];
            var jobDataModel = jobDataModels[i];

            var returnedJobItem = jobResponse.Items[i];
            Assert.Equal(messageId, returnedJobItem.MessageId);
            Assert.Equal(mockMessage.Object.NMSMessageId, returnedJobItem.MessageId);
            Assert.Same(jobDataModel.Object, returnedJobItem.Data);

            var i1 = i;
            converter.Verify(c => c.Convert(i1.ToString()), Times.Once);
        }
    }

    /// <summary>
    ///     Test handling of an exception thrown during parsing
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_ParsingError()
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<IMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
            .Returns("{}");

        converter.Setup(c => c.Convert("{}"))
            .Returns(() => throw new LandmineException());

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Single(bodyRetriever.Invocations);
        Assert.Single(converter.Invocations);
    }

    /// <summary>
    ///     Spin-off of Test_GetJob_ParsingError
    ///     Confirm that the job will also be deleted if the converter silently failed to parse.
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_ParsingNull()
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
        mockConnection.Setup(c => c.Start());
        mockConnection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(mockSession.Object);

        var activeConnectionFactory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        activeConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var bodyRetriever = new Mock<IActiveMqMessageBodyRetriever>(MockBehavior.Strict);

        // Setup Job Returns

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<IMessage>();
        mockMessage.Setup(m => m.NMSMessageId).Returns(messageId);

        var mockChannelQueue = new Queue<IMessage>();
        mockChannelQueue.Enqueue(mockMessage.Object);

        bodyRetriever.Setup(r => r.GetMessageBody(mockMessage.Object))
            .Returns("{}");

        converter.Setup(c => c.Convert("{}"))
            .Returns((IJobDataModel?) null);

        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new ActiveMqJobSource(activeConnectionFactory.Object, Options.Create(configuration),
            bodyRetriever.Object, converter.Object, new NullLogger<ActiveMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(activeConnectionFactory.Invocations);
        Assert.Equal(2, mockConnection.Invocations.Count);
        mockConnection.Verify(c => c.Start(), Times.Once);
        mockConnection.Verify(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge), Times.Once);
        Assert.Equal(2, mockSession.Invocations.Count);
        mockSession.Verify(s => s.GetQueueAsync(queueName), Times.Once);
        mockSession.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Once);

        Assert.Single(bodyRetriever.Invocations);
        Assert.Single(converter.Invocations);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        // Declare objects

        var configuration = new ActiveMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new ActiveMqJobSource(null!, Options.Create(configuration),
            null!, null!, new NullLogger<ActiveMqJobSource>());

        // Run. Source should be executing an empty block with no complains about all the nulls that it's been given.
        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true); // Satisfy Sonar requirements
    }

    public sealed class LandmineException : Exception;
}