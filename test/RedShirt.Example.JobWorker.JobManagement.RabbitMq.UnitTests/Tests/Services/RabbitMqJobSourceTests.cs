using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
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
    public async Task Test_AcknowledgeCompletionAsync()
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
            null!, new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 4321,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        await jobSource.AcknowledgeCompletionAsync(job, true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        mockChannel.Verify(c => c.BasicAckAsync(4321, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeCompletionAsync_Incompatible()
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
            null!, new NullLogger<RabbitMqJobSource>());

        var job = new Mock<IJobModel>();

        await jobSource.AcknowledgeCompletionAsync(job.Object, true, TestContext.Current.CancellationToken);

        Assert.Empty(rabbitConnectionFactory.Invocations);
        Assert.Empty(mockConnection.Invocations);
        Assert.Empty(mockChannel.Invocations);
        mockChannel.Verify(
            c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Test_AcknowledgeCompletionAsync_ObjectDisposedExceptionIgnored()
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
            null!, new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 4321,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        await jobSource.AcknowledgeCompletionAsync(job, true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        mockChannel.Verify(c => c.BasicAckAsync(4321, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeCompletionAsync_RegularExceptionNotIgnored()
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
            null!, new NullLogger<RabbitMqJobSource>());

        var job = new RabbitMqJobModel
        {
            MessageId = "1234",
            DeliveryTag = 1234,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        await Assert.ThrowsAsync<Exception>(async () =>
            await jobSource.AcknowledgeCompletionAsync(job, true, TestContext.Current.CancellationToken));

        // Assert
        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        mockChannel.Verify(c => c.BasicAckAsync(1234, false, TestContext.Current.CancellationToken), Times.Once);
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync((BasicGetResult?) null);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        Assert.Empty(converter.Invocations);
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

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

        var jobDataModel = new Mock<IJobDataModel>();

        converter
            .Setup(c => c.Convert(bodyString))
            .Returns(jobDataModel.Object);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal(messageId, returnedJobItem.IdempotencyId);
        Assert.Same(jobDataModel.Object, returnedJobItem.Data);
        Assert.Equal(deliveryTag, Assert.IsType<RabbitMqJobModel>(returnedJobItem).DeliveryTag);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Equal(2, mockChannel.Invocations.Count);

        Assert.Single(converter.Invocations);
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

        var mockChannelQueue = new Queue<BasicGetResult>();
        var deliveryTags = new List<ulong>();
        var messageIds = new List<string>();
        var jobDataModels = new List<Mock<IJobDataModel>>();

        for (var i = 0; i < batchSize; i++)
        {
            ulong deliveryTag = 1234 + (uint) i;
            deliveryTags.Add(deliveryTag);
            var messageId = Guid.NewGuid().ToString();
            messageIds.Add(messageId);
            var bodyString = $"_{deliveryTag}_";

            var jobDataModel = new Mock<IJobDataModel>();
            jobDataModels.Add(jobDataModel);

            var basicProperties = new Mock<IReadOnlyBasicProperties>();
            basicProperties.Setup(p => p.MessageId).Returns(messageId);

            mockChannelQueue.Enqueue(new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
                basicProperties.Object,
                new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

            converter
                .Setup(c => c.Convert(bodyString))
                .Returns(jobDataModel.Object);
        }

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(batchSize, jobResponse.Items.Count);

        for (var i = 0; i < batchSize; i++)
        {
            var deliveryTag = deliveryTags[i];
            var messageId = messageIds[i];
            var jobDataModel = jobDataModels[i];

            var returnedJobItem = jobResponse.Items[i];

            Assert.Equal(messageId, returnedJobItem.MessageId);
            Assert.Equal(messageId, returnedJobItem.IdempotencyId);
            Assert.Same(jobDataModel.Object, returnedJobItem.Data);
            Assert.Equal(deliveryTag, Assert.IsType<RabbitMqJobModel>(returnedJobItem).DeliveryTag);
        }

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Equal(batchSize, mockChannel.Invocations.Count);

        Assert.Equal(batchSize, converter.Invocations.Count);

        Assert.Empty(mockChannelQueue);
    }

    [Fact]
    public async Task Test_GetJobs_ParsingError()
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

        ulong deliveryTag = 1234;
        var bodyString = "{}";

        var mockChannelQueue = new Queue<BasicGetResult>();
        mockChannelQueue.Enqueue(new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
            new Mock<IReadOnlyBasicProperties>().Object,
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        mockChannel
            .Setup(c => c.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        converter
            .Setup(c => c.Convert(bodyString))
            .Returns(() => throw new LandmineException());

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Equal(3, mockChannel.Invocations.Count);

        Assert.Single(converter.Invocations);
    }

    /// <summary>
    ///     Spin-off of Test_GetJobs_ParsingError
    ///     Confirm that the job will also be deleted if the parser silently failed to parse.
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_ParsingNull()
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

        ulong deliveryTag = 1234;
        var bodyString = "{}";

        var mockChannelQueue = new Queue<BasicGetResult>();
        mockChannelQueue.Enqueue(new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
            new Mock<IReadOnlyBasicProperties>().Object,
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(() => mockChannelQueue.TryDequeue(out var job) ? job : null);

        mockChannel
            .Setup(c => c.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        converter
            .Setup(c => c.Convert(bodyString))
            .Returns((IJobDataModel?) null);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Equal(3, mockChannel.Invocations.Count);

        Assert.Single(converter.Invocations);
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
            null!, new NullLogger<RabbitMqJobSource>());

        // Run. Source should be executing an empty block with no complains about all the nulls that it's been given.
        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true); // Satisfy Sonar requirements
    }

    public sealed class LandmineException : Exception;
}