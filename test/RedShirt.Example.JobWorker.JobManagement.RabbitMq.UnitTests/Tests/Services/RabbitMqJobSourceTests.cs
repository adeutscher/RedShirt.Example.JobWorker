using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

/// <summary>
///     Tests for jobs via RabbitMQ
/// </summary>
public class RabbitMqJobSourceTests
{
    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        // Set up Mocks

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        // Declare objects

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 4321,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await jobSource.AcknowledgeAsync(job, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        mockChannel.Verify(c => c.BasicAckAsync(4321, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Incompatible()
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var job = new Mock<IRawJobModel>();

        await jobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.Empty(rabbitConnectionFactory.Invocations);
        Assert.Empty(mockConnection.Invocations);
        Assert.Empty(mockChannel.Invocations);
        mockChannel.Verify(
            c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.InvalidData)]
    public async Task Test_AcknowledgeAsync_NonRecoverable_NacksWithoutRequeue(CoreJobResult result)
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        mockChannel
            .Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 8888,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        mockChannel.Verify(c => c.BasicNackAsync(8888, false, false, TestContext.Current.CancellationToken),
            Times.Once);
        mockChannel.Verify(
            c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_ObjectDisposedExceptionIgnored()
    {
        // Set up Mocks

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new ObjectDisposedException("test"));

        // Declare objects

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 4321,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await jobSource.AcknowledgeAsync(job, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        mockChannel.Verify(c => c.BasicAckAsync(4321, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_Recoverable_NacksWithRequeue(CoreJobResult result)
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        mockChannel
            .Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 9999,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        mockChannel.Verify(c => c.BasicNackAsync(9999, false, true, TestContext.Current.CancellationToken),
            Times.Once);
        mockChannel.Verify(
            c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_RegularExceptionNotIgnored()
    {
        // Set up Mocks

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new Exception("test"));

        // Declare objects

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 1234,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await Assert.ThrowsAsync<Exception>(async () =>
            await jobSource.AcknowledgeAsync(job, CoreJobResult.Success, TestContext.Current.CancellationToken));

        // Assert
        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        mockChannel.Verify(c => c.BasicAckAsync(1234, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_AlreadyClosedException_AfterPartialBatch_ReturnsCollectedJobs()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        ulong deliveryTag = 77;
        var messageId = Guid.NewGuid().ToString();
        var bodyString = "{}";

        var basicProperties = new Mock<IReadOnlyBasicProperties>();
        basicProperties.Setup(p => p.MessageId).Returns(messageId);

        var getCalls = 0;
        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() =>
            {
                getCalls++;
                if (getCalls == 1)
                {
                    return new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
                        basicProperties.Object,
                        new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString)));
                }

                throw new AlreadyClosedException(
                    new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED"));
            });

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal(messageId, returnedJobItem.IdempotencyId);
        Assert.Equal(bodyString, returnedJobItem.Body);
        Assert.Equal(2, getCalls);
    }

    [Fact]
    public async Task Test_GetJobs_AlreadyClosedException_ReturnsEmpty()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ThrowsAsync(new AlreadyClosedException(
                new ShutdownEventArgs(ShutdownInitiator.Application, 0, "closed")));

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(jobResponse.Items);
        mockChannel.Verify(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        // Setup Job Returns

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync((BasicGetResult?) null);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_GotJob()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        // Setup Job Returns

        ulong deliveryTag = 1234;
        var messageId = Guid.NewGuid().ToString();
        var bodyString = "{}";

        var basicProperties = new Mock<IReadOnlyBasicProperties>();
        basicProperties.Setup(p => p.MessageId).Returns(messageId);

        var mockChannelQueue = new Queue<BasicGetResult>();
        mockChannelQueue.Enqueue(new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
            basicProperties.Object,
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal(messageId, returnedJobItem.IdempotencyId);
        Assert.Equal(bodyString, returnedJobItem.Body);
        Assert.Equal(deliveryTag, Assert.IsType<RabbitMqJobModel>(returnedJobItem).DeliveryTag);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Equal(2, mockChannel.Invocations.Count);
    }

    /// <summary>
    ///     Test of getting a multiple jobs in a single GetJobs call.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task Test_GetJobs_GotMultipleJobs(int batchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        // Setup Job Returns

        var mockChannelQueue = new Queue<BasicGetResult>();
        var deliveryTags = new List<ulong>();
        var messageIds = new List<string>();
        var bodyStrings = new List<string>();

        for (var i = 0; i < batchSize; i++)
        {
            ulong deliveryTag = 1234 + (uint) i;
            deliveryTags.Add(deliveryTag);
            var messageId = Guid.NewGuid().ToString();
            messageIds.Add(messageId);
            var bodyString = $"_{deliveryTag}_";
            bodyStrings.Add(bodyString);

            var basicProperties = new Mock<IReadOnlyBasicProperties>();
            basicProperties.Setup(p => p.MessageId).Returns(messageId);

            mockChannelQueue.Enqueue(new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
                basicProperties.Object,
                new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));
        }

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(batchSize, jobResponse.Items.Count);

        for (var i = 0; i < batchSize; i++)
        {
            var deliveryTag = deliveryTags[i];
            var messageId = messageIds[i];
            var bodyString = bodyStrings[i];

            var returnedJobItem = jobResponse.Items[i];

            Assert.Equal(messageId, returnedJobItem.MessageId);
            Assert.Equal(messageId, returnedJobItem.IdempotencyId);
            Assert.Equal(bodyString, returnedJobItem.Body);
            Assert.Equal(deliveryTag, Assert.IsType<RabbitMqJobModel>(returnedJobItem).DeliveryTag);
        }

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Equal(batchSize, mockChannel.Invocations.Count);

        Assert.Empty(mockChannelQueue);
    }

    [Fact]
    public async Task Test_GetJobs_MissingMessageId_UsesUnknownAndNullIdempotencyId()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = queueName
        };

        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);
        mockConnection.Setup(c => c.CreateChannelAsync())
            .ReturnsAsync(mockChannel.Object);

        var rabbitConnectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        rabbitConnectionFactory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        ulong deliveryTag = 55;
        var bodyString = "{}";

        var basicProperties = new Mock<IReadOnlyBasicProperties>();
        basicProperties.Setup(p => p.MessageId).Returns((string?) null);

        var mockChannelQueue = new Queue<BasicGetResult>();
        mockChannelQueue.Enqueue(new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
            basicProperties.Object,
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal("UNKNOWN", returnedJobItem.MessageId);
        Assert.Null(returnedJobItem.IdempotencyId);
        Assert.Equal(bodyString, returnedJobItem.Body);
        Assert.Equal(deliveryTag, Assert.IsType<RabbitMqJobModel>(returnedJobItem).DeliveryTag);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        // Declare objects

        var configuration = new RabbitMqJobSource.ConfigurationModel
        {
            QueueName = null! // moot
        };

        var jobSource = new RabbitMqJobSource(null!, Options.Create(configuration),
            new NullLogger<RabbitMqJobSource>());

        // Run. Source should be executing an empty block with no complains about all the nulls that it's been given.
        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
    }
}