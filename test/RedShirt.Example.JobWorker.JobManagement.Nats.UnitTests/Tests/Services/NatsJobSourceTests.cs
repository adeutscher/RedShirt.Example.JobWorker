using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsJobSourceTests
{
    [Fact]
    public async Task Test_AcknowledgeCompletionAsync()
    {
        // Set up Mocks

        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();

        // Declare objects
        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null!
        };

        var natsJobSource = new NatsJobSource(null!, null!, null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobModel = new NatsRawJobModel
        {
            Message = message.Object,
            MessageId = "moot",
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
        };

        await natsJobSource.AcknowledgeCompletionAsync(jobModel, true,
            TestContext.Current.CancellationToken);

        message.Verify(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeCompletionAsync_Incompatible()
    {
        // Set up Mocks

        var job = new Mock<IJobModel>();

        // Declare objects
        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null!
        };

        var natsJobSource = new NatsJobSource(null!, null!, null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        await natsJobSource.AcknowledgeCompletionAsync(job.Object, true,
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

        var mockBodyRetriever = new Mock<IBodyRetriever>();

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => new INatsJSMsg<NatsMemoryOwner<byte>>[] { }.ToAsyncEnumerable());

        // Declare objects

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object, mockBodyRetriever.Object,
            converter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        Assert.Single(mockContextFactory.Invocations);
        Assert.Single(mockContext.Invocations);
        mockContext.Verify(
            c => c.CreateOrUpdateConsumerAsync(It.IsAny<string>(), It.IsAny<ConsumerConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(mockConsumer.Invocations);
        Assert.Single(mockGetter.Invocations);

        Assert.Empty(mockBodyRetriever.Invocations);
        Assert.Empty(converter.Invocations);
    }

    /// <summary>
    ///     Test of getting jobs
    /// </summary>
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

        var mockBodyRetriever = new Mock<IBodyRetriever>(MockBehavior.Strict);

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

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

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>> {mockMessage.Object};

        var jobDataModel = new Mock<IJobDataModel>();

        mockBodyRetriever.Setup(br => br.GetMessageBody(mockMessage.Object))
            .Returns("{___}");

        converter.Setup(c => c.Convert("{___}"))
            .Returns(jobDataModel.Object);

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        // Declare objects

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object, mockBodyRetriever.Object,
            converter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Same(jobDataModel.Object, returnedJobItem.Data);

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

        Assert.Single(mockBodyRetriever.Invocations);
        Assert.Single(converter.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job
    /// </summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public async Task Test_GetJobs_GotJob_Exception(int batchSize, int expectedBatchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName
        };

        var mockBodyRetriever = new Mock<IBodyRetriever>(MockBehavior.Strict);

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>> {mockMessage.Object};

        mockBodyRetriever.Setup(br => br.GetMessageBody(mockMessage.Object))
            .Returns("{___}");

        converter.Setup(c => c.Convert("{___}"))
            .Returns(() => throw new LandmineException());

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        // Declare objects

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object, mockBodyRetriever.Object,
            converter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

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

        Assert.Single(mockBodyRetriever.Invocations);
        Assert.Single(converter.Invocations);

        mockMessage.Verify(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     MessageId falls back to UNKNOWN when JetStream metadata is missing.
    /// </summary>
    [Fact]
    public async Task Test_GetJobs_GotJob_MessageIdUnknownWhenMetadataMissing()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName
        };

        var mockBodyRetriever = new Mock<IBodyRetriever>(MockBehavior.Strict);

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockMessage.Setup(m => m.Metadata).Returns((NatsJSMsgMetadata?) null);

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>> {mockMessage.Object};

        var jobDataModel = new Mock<IJobDataModel>();

        mockBodyRetriever.Setup(br => br.GetMessageBody(mockMessage.Object))
            .Returns("{___}");

        converter.Setup(c => c.Convert("{___}"))
            .Returns(jobDataModel.Object);

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object, mockBodyRetriever.Object,
            converter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal("UNKNOWN", returnedJobItem.MessageId);
        Assert.Same(jobDataModel.Object, returnedJobItem.Data);
    }

    /// <summary>
    ///     Test of getting multiple jobs
    /// </summary>
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

        var mockBodyRetriever = new Mock<IBodyRetriever>(MockBehavior.Strict);

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        // Setup Job Returns

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
            mockData.Add(mockMessage.Object);

            var jobDataModel = new Mock<IJobDataModel>();

            mockBodyRetriever.Setup(br => br.GetMessageBody(mockMessage.Object))
                .Returns("{___}");

            converter.Setup(c => c.Convert("{___}"))
                .Returns(jobDataModel.Object);
        }

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => mockData.ToAsyncEnumerable());

        // Declare objects

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object, mockBodyRetriever.Object,
            converter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(expectedBatchSize, jobResponse.Items.Count);

        for (var i = 0; i < expectedBatchSize; i++)
        {
            Assert.Single(jobResponse.Items, item => item.MessageId == messageIds[i]);
        }

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

        Assert.Equal(expectedBatchSize, mockBodyRetriever.Invocations.Count);
        Assert.Equal(expectedBatchSize, converter.Invocations.Count);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        // Declare objects

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null! // moot
        };

        var jobSource = new NatsJobSource(null!, null!,
            null!, null!, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        // Run. Source should be executing an empty block with no complains about all the nulls that it's been given.
        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
    }

    public sealed class LandmineException : Exception;
}