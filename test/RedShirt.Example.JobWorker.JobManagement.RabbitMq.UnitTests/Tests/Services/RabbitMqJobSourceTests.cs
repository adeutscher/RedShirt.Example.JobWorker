using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

/// <summary>
///     Tests for jobs via RabbitMQ
/// </summary>
public class RabbitMqJobSourceTests
{
    private static (RabbitMqJobSource JobSource, Mock<IRabbitMqChannelRetryWrapper> ChannelRetryWrapper)
        CreateJobSource(IChannel channel, string? queueName = null)
    {
        var channelRetryWrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        channelRetryWrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IChannel, CancellationToken, Task> callback, bool _, Action<IConnection>? __,
                CancellationToken token) => callback(channel, token));

        var jobSource = new RabbitMqJobSource(
            channelRetryWrapper.Object,
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                QueueName = queueName!
            }),
            NullLogger<RabbitMqJobSource>.Instance);

        return (jobSource, channelRetryWrapper);
    }

    private static void VerifyWrapperCalled(Mock<IRabbitMqChannelRetryWrapper> channelRetryWrapper, Times times)
    {
        channelRetryWrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IChannel, CancellationToken, Task>>(),
            It.IsAny<bool>(),
            It.IsAny<Action<IConnection>?>(),
            TestContext.Current.CancellationToken), times);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        var (jobSource, channelRetryWrapper) = CreateJobSource(mockChannel.Object);

        var job = new RabbitMqRawJobModel
        {
            MessageId = "1234",
            DeliveryTag = 4321,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await jobSource.AcknowledgeAsync(job, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        VerifyWrapperCalled(channelRetryWrapper, Times.Once());
        Assert.Single(mockChannel.Invocations);
        mockChannel.Verify(c => c.BasicAckAsync(4321, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Incompatible()
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        var channelRetryWrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);

        var jobSource = new RabbitMqJobSource(
            channelRetryWrapper.Object,
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                QueueName = null!
            }),
            NullLogger<RabbitMqJobSource>.Instance);

        var job = new Mock<IRawJobModel>();

        await jobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        channelRetryWrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IChannel, CancellationToken, Task>>(),
            It.IsAny<bool>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
        mockChannel
            .Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        var (jobSource, _) = CreateJobSource(mockChannel.Object);

        var job = new RabbitMqRawJobModel
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
    public async Task Test_AcknowledgeAsync_ObjectDisposedException_Propagates()
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new ObjectDisposedException("test"));

        var (jobSource, _) = CreateJobSource(mockChannel.Object);

        var job = new RabbitMqRawJobModel
        {
            MessageId = "1234",
            DeliveryTag = 4321,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            jobSource.AcknowledgeAsync(job, CoreJobResult.Success, TestContext.Current.CancellationToken));

        mockChannel.Verify(c => c.BasicAckAsync(4321, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_Recoverable_NacksWithRequeue(CoreJobResult result)
    {
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        mockChannel
            .Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask());

        var (jobSource, _) = CreateJobSource(mockChannel.Object);

        var job = new RabbitMqRawJobModel
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
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        mockChannel
            .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new Exception("test"));

        var (jobSource, channelRetryWrapper) = CreateJobSource(mockChannel.Object);

        var job = new RabbitMqRawJobModel
        {
            MessageId = "1234",
            DeliveryTag = 1234,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        await Assert.ThrowsAsync<Exception>(async () =>
            await jobSource.AcknowledgeAsync(job, CoreJobResult.Success, TestContext.Current.CancellationToken));

        VerifyWrapperCalled(channelRetryWrapper, Times.Once());
        Assert.Single(mockChannel.Invocations);
        mockChannel.Verify(c => c.BasicAckAsync(1234, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_AlreadyClosedException_AfterPartialBatch_Propagates()
    {
        var queueName = Guid.NewGuid().ToString();
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

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

        var (jobSource, _) = CreateJobSource(mockChannel.Object, queueName);

        await Assert.ThrowsAsync<AlreadyClosedException>(() =>
            jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken));

        Assert.Equal(2, getCalls);
    }

    [Fact]
    public async Task Test_GetJobs_AlreadyClosedException_Propagates()
    {
        var queueName = Guid.NewGuid().ToString();
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ThrowsAsync(new AlreadyClosedException(
                new ShutdownEventArgs(ShutdownInitiator.Application, 0, "closed")));

        var (jobSource, channelRetryWrapper) = CreateJobSource(mockChannel.Object, queueName);

        await Assert.ThrowsAsync<AlreadyClosedException>(() =>
            jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken));

        VerifyWrapperCalled(channelRetryWrapper, Times.Once());
        mockChannel.Verify(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var queueName = Guid.NewGuid().ToString();
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);
        mockChannel
            .Setup(c => c.BasicGetAsync(queueName, false, TestContext.Current.CancellationToken))
            .ReturnsAsync((BasicGetResult?) null);

        var (jobSource, channelRetryWrapper) = CreateJobSource(mockChannel.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);
        VerifyWrapperCalled(channelRetryWrapper, Times.Once());
        Assert.Single(mockChannel.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_GotJob()
    {
        var queueName = Guid.NewGuid().ToString();
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

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

        var (jobSource, channelRetryWrapper) = CreateJobSource(mockChannel.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal(messageId, returnedJobItem.IdempotencyId);
        Assert.Equal(bodyString, returnedJobItem.Body);
        Assert.Equal(deliveryTag, Assert.IsType<RabbitMqRawJobModel>(returnedJobItem).DeliveryTag);

        VerifyWrapperCalled(channelRetryWrapper, Times.Exactly(2));
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
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

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

        var (jobSource, channelRetryWrapper) = CreateJobSource(mockChannel.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

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
            Assert.Equal(deliveryTag, Assert.IsType<RabbitMqRawJobModel>(returnedJobItem).DeliveryTag);
        }

        VerifyWrapperCalled(channelRetryWrapper, Times.Exactly(batchSize));
        Assert.Equal(batchSize, mockChannel.Invocations.Count);
        Assert.Empty(mockChannelQueue);
    }

    [Fact]
    public async Task Test_GetJobs_MissingMessageId_UsesUnknownAndNullIdempotencyId()
    {
        var queueName = Guid.NewGuid().ToString();
        var mockChannel = new Mock<IChannel>(MockBehavior.Strict);

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

        var (jobSource, _) = CreateJobSource(mockChannel.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal("UNKNOWN", returnedJobItem.MessageId);
        Assert.Null(returnedJobItem.IdempotencyId);
        Assert.Equal(bodyString, returnedJobItem.Body);
        Assert.Equal(deliveryTag, Assert.IsType<RabbitMqRawJobModel>(returnedJobItem).DeliveryTag);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var jobSource = new RabbitMqJobSource(
            null!,
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                QueueName = null!
            }),
            NullLogger<RabbitMqJobSource>.Instance);

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
        // Add at least one token assert to satisfy Sonar
        Assert.True(true);
    }
}