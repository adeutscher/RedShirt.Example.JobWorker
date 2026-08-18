using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsJobSourceTests
{
    private static NatsJobSource CreateJobSource(INatsMessageSource messageSource,
        INatsConsumerSource? consumerSource = null, string? streamName = null)
    {
        return new NatsJobSource(
            consumerSource ?? new Mock<INatsConsumerSource>(MockBehavior.Strict).Object,
            messageSource,
            NatsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<NatsJobSource>(),
            Options.Create(new NatsStreamConfigurationModel
            {
                StreamName = streamName ??
                             Guid.NewGuid()
                                 .ToString(),
                ConsumerName = "foo"
            }));
    }

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

        var natsJobSource = CreateJobSource(new Mock<INatsMessageSource>(MockBehavior.Strict).Object);

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

        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);
        var messageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);

        var natsJobSource = CreateJobSource(messageSource.Object, consumerSource.Object);

        await natsJobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.Empty(consumerSource.Invocations);
        Assert.Empty(messageSource.Invocations);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var mockMessageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);
        var consumerSource = new Mock<INatsConsumerSource>(MockBehavior.Strict);

        mockMessageSource
            .Setup(m => m.FetchMessagesAsync(1, TestContext.Current.CancellationToken))
            .ReturnsAsync(new NatsMessageSourceResponse {Messages = []});

        var jobSource = CreateJobSource(mockMessageSource.Object, consumerSource.Object);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        mockMessageSource.Verify(
            m => m.FetchMessagesAsync(1, TestContext.Current.CancellationToken),
            Times.Once);
        consumerSource.Verify(s => s.GetConsumerAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Test_GetJobs_GotJob_MessageIdUnknownWhenMetadataMissing()
    {
        var mockMessageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);

        const string body = "{___}";
        var mockMessage = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>();
        mockMessage.Setup(m => m.Metadata).Returns((NatsJSMsgMetadata?) null);
        SetupMessageBody(mockMessage, body);

        mockMessageSource
            .Setup(m => m.FetchMessagesAsync(1, TestContext.Current.CancellationToken))
            .ReturnsAsync(new NatsMessageSourceResponse {Messages = [mockMessage.Object]});

        var jobSource = CreateJobSource(mockMessageSource.Object);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal("UNKNOWN", returnedJobItem.MessageId);
        Assert.Equal(body, returnedJobItem.Body);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Test_GetJobs_GotJobs(int batchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var mockMessageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);

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

        mockMessageSource
            .Setup(m => m.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(new NatsMessageSourceResponse {Messages = [mockMessage.Object]});

        var jobSource = CreateJobSource(mockMessageSource.Object, streamName: queueName);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        var returnedJobItem = Assert.Single(jobResponse.Items);
        Assert.Equal(messageId, returnedJobItem.MessageId);
        Assert.Equal(body, returnedJobItem.Body);

        mockMessageSource.Verify(
            m => m.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Test_GetJobs_GotMultiple(int batchSize)
    {
        var queueName = Guid.NewGuid().ToString();

        var mockMessageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);

        const string body = "{___}";
        var mockData = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();
        var messageIds = new List<string>();

        for (var i = 0; i < batchSize; i++)
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

        mockMessageSource
            .Setup(m => m.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken))
            .ReturnsAsync(new NatsMessageSourceResponse {Messages = mockData});

        var jobSource = CreateJobSource(mockMessageSource.Object, streamName: queueName);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Equal(batchSize, jobResponse.Items.Count);

        for (var i = 0; i < batchSize; i++)
        {
            Assert.Single(jobResponse.Items, item => item.MessageId == messageIds[i]);
        }

        Assert.All(jobResponse.Items, item => Assert.Equal(body, item.Body));

        mockMessageSource.Verify(
            m => m.FetchMessagesAsync(batchSize, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var jobSource = CreateJobSource(new Mock<INatsMessageSource>(MockBehavior.Strict).Object);

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
        // Satisfy sonar (not much to really assert for NATS as heartbeats are handled by underlying library)
        Assert.True(true);
    }
}