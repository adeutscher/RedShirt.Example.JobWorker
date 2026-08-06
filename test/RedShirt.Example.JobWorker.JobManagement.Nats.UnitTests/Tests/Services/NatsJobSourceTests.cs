using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Utility;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsJobSourceTests
{
    private static void SetupMessageBody(Mock<INatsJSMsg<NatsMemoryOwner<byte>>> mockMessage, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var owner = NatsMemoryOwner<byte>.Allocate(bytes.Length);
        bytes.AsSpan().CopyTo(owner.Span);
        mockMessage.Setup(m => m.Data).Returns(owner);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    [InlineData(CoreJobResult.InvalidData)]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    public async Task Test_AcknowledgeAsync_AlwaysAcks(CoreJobResult result)
    {
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null!
        };

        var natsJobSource = new NatsJobSource(null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobModel = new NatsRawJobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow
        };

        await natsJobSource.AcknowledgeAsync(jobModel, result,
            TestContext.Current.CancellationToken);

        message.Verify(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken), Times.Once);
        message.Verify(m => m.NakAsync(It.IsAny<AckOpts?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Incompatible()
    {
        var job = new Mock<IRawJobModel>();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null!
        };

        var natsJobSource = new NatsJobSource(null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        await natsJobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName
        };

        var mockGetter = new Mock<IFetchNoWaitGetter>();

        var mockConsumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);

        var mockContext = new Mock<INatsJSContext>(MockBehavior.Strict);
        mockContext
            .Setup(c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        var mockContextFactory = new Mock<INatsJetStreamContextFactory>(MockBehavior.Strict);
        mockContextFactory
            .Setup(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => new INatsJSMsg<NatsMemoryOwner<byte>>[] { }.ToAsyncEnumerable());

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(mockContextFactory.Invocations);
        Assert.Single(mockContext.Invocations);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(mockConsumer.Invocations);
        Assert.Single(mockGetter.Invocations);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public async Task Test_GetJobs_GotJob(int batchSize, int expectedBatchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName
        };

        var mockGetter = new Mock<IFetchNoWaitGetter>();

        var mockConsumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);

        var mockContext = new Mock<INatsJSContext>(MockBehavior.Strict);
        mockContext
            .Setup(c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        var mockContextFactory = new Mock<INatsJetStreamContextFactory>(MockBehavior.Strict);
        mockContextFactory
            .Setup(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        const string body = "{___}";
        var streamSequence = (ulong) Random.Shared.NextInt64(1, long.MaxValue);
        var messageId = streamSequence.ToString();
        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockMessage.Setup(m => m.Metadata).Returns(new NatsJSMsgMetadata(
            new NatsJSSequencePair(streamSequence, 1),
            1,
            0,
            DateTimeOffset.UtcNow,
            queueName,
            "c1",
            string.Empty));
        SetupMessageBody(mockMessage, body);

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>> {mockMessage.Object};

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal(body, returnedJobItem.Body);

        Assert.Single(mockContextFactory.Invocations);
        Assert.Single(mockContext.Invocations);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);

        Assert.Empty(mockConsumer.Invocations);
        Assert.Single(mockGetter.Invocations);
        mockGetter
            .Verify(
                g => g.FetchNoWaitAsync(mockConsumer.Object,
                    It.Is<NatsJSFetchOpts>(o => o.MaxMsgs == expectedBatchSize),
                    It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test_GetJobs_GotJob_MessageIdUnknownWhenMetadataMissing()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName
        };

        var mockGetter = new Mock<IFetchNoWaitGetter>();

        var mockConsumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);

        var mockContext = new Mock<INatsJSContext>(MockBehavior.Strict);
        mockContext
            .Setup(c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        var mockContextFactory = new Mock<INatsJetStreamContextFactory>(MockBehavior.Strict);
        mockContextFactory
            .Setup(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        const string body = "{___}";
        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockMessage.Setup(m => m.Metadata).Returns((NatsJSMsgMetadata?) null);
        SetupMessageBody(mockMessage, body);

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>> {mockMessage.Object};

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal("UNKNOWN", returnedJobItem.MessageId);
        Assert.Equal(body, returnedJobItem.Body);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public async Task Test_GetJobs_GotMultiple(int batchSize, int expectedBatchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName
        };

        var mockGetter = new Mock<IFetchNoWaitGetter>();

        var mockConsumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);

        var mockContext = new Mock<INatsJSContext>(MockBehavior.Strict);
        mockContext
            .Setup(c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        var mockContextFactory = new Mock<INatsJetStreamContextFactory>(MockBehavior.Strict);
        mockContextFactory
            .Setup(f => f.CreateNatsJetStreamContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        const string body = "{___}";
        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();
        var messageIds = new List<string>();

        for (var i = 0; i < expectedBatchSize; i++)
        {
            var streamSequence = (ulong) (i + 1);
            var messageId = streamSequence.ToString();
            messageIds.Add(messageId);
            var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
            mockMessage.Setup(m => m.Metadata).Returns(new NatsJSMsgMetadata(
                new NatsJSSequencePair(streamSequence, (ulong) (i + 1)),
                1,
                0,
                DateTimeOffset.UtcNow,
                queueName,
                "c1",
                string.Empty));
            SetupMessageBody(mockMessage, body);
            mockData.Add(mockMessage.Object);
        }

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(expectedBatchSize, jobResponse.Items.Count);

        for (var i = 0; i < expectedBatchSize; i++)
        {
            Assert.Single(jobResponse.Items, item => item.MessageId == messageIds[i]);
        }

        Assert.All(jobResponse.Items, item => Assert.Equal(body, item.Body));

        Assert.Single(mockContextFactory.Invocations);
        Assert.Single(mockContext.Invocations);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);

        Assert.Empty(mockConsumer.Invocations);
        Assert.Single(mockGetter.Invocations);
        mockGetter
            .Verify(
                g => g.FetchNoWaitAsync(mockConsumer.Object,
                    It.Is<NatsJSFetchOpts>(o => o.MaxMsgs == expectedBatchSize),
                    It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null!
        };

        var jobSource = new NatsJobSource(null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
    }
}