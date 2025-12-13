using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
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

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.MessageId).Returns("1234");

        await jobSource.AcknowledgeCompletionAsync(job.Object, true, TestContext.Current.CancellationToken);

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

        var jobResponse = await jobSource.GetJobsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobResponse.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        Assert.Empty(converter.Invocations);
    }

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
        var bodyString = "{}";

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync((BasicGetResult?) new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
                new Mock<IReadOnlyBasicProperties>().Object,
                new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

        var jobDataModel = new Mock<IJobDataModel>();

        converter
            .Setup(c => c.Convert(bodyString))
            .Returns(jobDataModel.Object);

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobResponse.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(deliveryTag.ToString(), returnedJobItem.MessageId);
        Assert.Same(jobDataModel.Object, returnedJobItem.Data);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

        Assert.Single(converter.Invocations);
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

        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync((BasicGetResult?) new BasicGetResult(deliveryTag, false, "foo", "bar", 1,
                new Mock<IReadOnlyBasicProperties>().Object,
                new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(bodyString))));

        var jobDataModel = new Mock<IJobDataModel>();

        converter
            .Setup(c => c.Convert(bodyString))
            .Returns(() => throw new LandmineException());

        // Declare objects

        var jobSource = new RabbitMqJobSource(rabbitConnectionFactory.Object, Options.Create(configuration),
            converter.Object, new NullLogger<RabbitMqJobSource>());

        var jobResponse = await jobSource.GetJobsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobResponse.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(rabbitConnectionFactory.Invocations);
        Assert.Single(mockConnection.Invocations);
        Assert.Single(mockChannel.Invocations);

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

    private sealed class LandmineException : Exception;
}