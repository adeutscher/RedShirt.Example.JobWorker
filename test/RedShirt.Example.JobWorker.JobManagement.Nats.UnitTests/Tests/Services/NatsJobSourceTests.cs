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
    private static NatsJobSource CreateJobSource(INatsMessageSource messageSource, string? streamName = null,
        int visibilityTimeoutSeconds = 20)
    {
        return new NatsJobSource(
            messageSource,
            NatsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<NatsJobSource>(),
            Options.Create(new NatsStreamConfigurationModel
            {
                StreamName = streamName ??
                             Guid.NewGuid()
                                 .ToString(),
                ConsumerName = "foo"
            }),
            Options.Create(new NatsStreamTimeoutConfigurationModel
            {
                VisibilityTimeoutSeconds = visibilityTimeoutSeconds
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
    [InlineData(10, 20, 15)]
    [InlineData(40, 40, 30)]
    public void RecommendedHeartbeatIntervalSeconds_IsThreeQuartersOfEffectiveVisibility(
        int configuredSeconds, int effectiveSeconds, int expectedHeartbeat)
    {
        var configuration = new NatsStreamTimeoutConfigurationModel
        {
            VisibilityTimeoutSeconds = configuredSeconds
        };
        Assert.Equal(effectiveSeconds, configuration.EffectiveVisibilityTimeoutSeconds);

        var jobSource = CreateJobSource(new Mock<INatsMessageSource>(MockBehavior.Strict).Object,
            visibilityTimeoutSeconds: configuredSeconds);

        Assert.Equal(expectedHeartbeat, jobSource.RecommendedHeartbeatIntervalSeconds);
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

        var messageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);

        var natsJobSource = CreateJobSource(messageSource.Object);

        await natsJobSource.AcknowledgeAsync(job.Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.Empty(messageSource.Invocations);
    }

    [Fact]
    public async Task Test_GetJobs_GetNoJobs()
    {
        var mockMessageSource = new Mock<INatsMessageSource>(MockBehavior.Strict);

        mockMessageSource
            .Setup(m => m.FetchMessagesAsync(1, TestContext.Current.CancellationToken))
            .ReturnsAsync(new NatsMessageSourceResponse {Messages = []});

        var jobSource = CreateJobSource(mockMessageSource.Object);

        var jobResponse = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(jobResponse.Items);

        mockMessageSource.Verify(
            m => m.FetchMessagesAsync(1, TestContext.Current.CancellationToken),
            Times.Once);
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

        var jobSource = CreateJobSource(mockMessageSource.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
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

        var jobSource = CreateJobSource(mockMessageSource.Object, queueName);

        var jobResponse = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
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
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>(MockBehavior.Strict);
        message
            .Setup(m => m.AckProgressAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken))
            .Returns(ValueTask.CompletedTask);

        var jobSource = CreateJobSource(new Mock<INatsMessageSource>(MockBehavior.Strict).Object);

        await jobSource.HeartbeatAsync(new NatsRawJobModel
        {
            Message = message.Object,
            MessageId = "m",
            CreatedAtUtc = DateTime.UtcNow
        }, TestContext.Current.CancellationToken);

        message.Verify(m => m.AckProgressAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync_Incompatible()
    {
        var jobSource = CreateJobSource(new Mock<INatsMessageSource>(MockBehavior.Strict).Object);

        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);
    }
}