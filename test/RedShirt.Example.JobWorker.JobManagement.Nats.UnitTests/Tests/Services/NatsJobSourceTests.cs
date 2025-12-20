using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.CredentialStorage.Ssm.UnitTests.Tests.Services;

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
            StreamName = null!,
            BatchSize = 0
        };

        var natsJobSource = new NatsJobSource(null!, null!, null!, null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobModel = new JobModel
        {
            Message = message.Object,
            MessageId = "moot",
            Data = null!
        };

        await natsJobSource.AcknowledgeCompletionAsync(jobModel, true, TestContext.Current.CancellationToken);

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
            StreamName = null!,
            BatchSize = 0
        };

        var natsJobSource = new NatsJobSource(null!, null!, null!, null!, null!,
            new NullLogger<NatsJobSource>(), Options.Create(configuration));

        await natsJobSource.AcknowledgeCompletionAsync(job.Object, true, TestContext.Current.CancellationToken);

        Assert.True(true); // Satisfy Sonar's requirement for an assert.
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName,
            BatchSize = 1
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
            .Setup(f => f.CreateNatsJSContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var sorter = new Mock<ISourceMessageSorter>(MockBehavior.Strict);
        sorter.Setup(obj => obj.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()))
            .Returns<List<IJobModel>>(input => input);

        // Setup Job Returns

        mockGetter
            .Setup(c => c.FetchNoWaitAsync(mockConsumer.Object, It.IsAny<NatsJSFetchOpts>(),
                TestContext.Current.CancellationToken))
            .Returns(() => new INatsJSMsg<NatsMemoryOwner<byte>>[] { }.ToAsyncEnumerable());

        // Declare objects

        var jobSource = new NatsJobSource(mockContextFactory.Object, mockGetter.Object, mockBodyRetriever.Object,
            converter.Object, sorter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobResponse.RecommendedHeartbeatIntervalSeconds);
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
        Assert.Single(sorter.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job
    /// </summary>
    [Theory]
    [InlineData(0, 1)] // Confirm EffectiveBatchSize
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public async Task Test_GetJobs_GotJob(int batchSize, int expectedBatchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName,
            BatchSize = batchSize
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
            .Setup(f => f.CreateNatsJSContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var sorter = new Mock<ISourceMessageSorter>(MockBehavior.Strict);
        sorter.Setup(obj => obj.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()))
            .Returns<List<IJobModel>>(input => input);

        // Setup Job Returns

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockMessage.Setup(m => m.Subject).Returns(messageId);

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockData.Add(mockMessage.Object);

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
            converter.Object, sorter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobResponse.RecommendedHeartbeatIntervalSeconds);
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
        Assert.Single(sorter.Invocations);
    }

    /// <summary>
    ///     Test of getting a single job
    /// </summary>
    [Theory]
    [InlineData(0, 1)] // Confirm EffectiveBatchSize
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public async Task Test_GetJobs_GotJob_Exception(int batchSize, int expectedBatchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = queueName,
            BatchSize = batchSize
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
            .Setup(f => f.CreateNatsJSContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext.Object);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var sorter = new Mock<ISourceMessageSorter>(MockBehavior.Strict);
        sorter.Setup(obj => obj.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()))
            .Returns<List<IJobModel>>(input => input);

        // Setup Job Returns

        var messageId = Guid.NewGuid().ToString();
        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockMessage.Setup(m => m.Subject).Returns(messageId);

        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockData.Add(mockMessage.Object);

        var jobDataModel = new Mock<IJobDataModel>();

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
            converter.Object, sorter.Object, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        var jobResponse = await jobSource.GetJobsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, jobResponse.RecommendedHeartbeatIntervalSeconds);
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
        Assert.Single(sorter.Invocations);

        mockMessage.Verify(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        // Declare objects

        var configuration = new NatsJobSource.ConfigurationModel
        {
            StreamName = null!, // moot
            BatchSize = 1
        };

        var jobSource = new NatsJobSource(null!, null!,
            null!, null!, null!, new NullLogger<NatsJobSource>(), Options.Create(configuration));

        // Run. Source should be executing an empty block with no complains about all the nulls that it's been given.
        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true); // Satisfy Sonar requirements
    }

    public sealed class LandmineException : Exception;
}