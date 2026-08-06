using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Enums;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubJobSourceTests
{
    private static GooglePubSubConfigurationModel DefaultOptions(int visibilityTimeoutSeconds = 60)
    {
        return new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            VisibilityTimeoutSeconds = visibilityTimeoutSeconds,
            DlqNotEnabled = true,
            MaximumReceives = 3
        };
    }

    private static Mock<IGooglePubSubPoisonMessagesHandler> CreatePassthroughPoisonHandler(
        PoisonEnforcementResult result = PoisonEnforcementResult.NotEnforced)
    {
        var poison = new Mock<IGooglePubSubPoisonMessagesHandler>(MockBehavior.Strict);
        poison
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<IPubSubMessageContainer>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return poison;
    }

    private static GooglePubSubJobModel CreateJob(IPubSubMessageContainer message)
    {
        return new GooglePubSubJobModel
        {
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static IPubSubMessageContainer CreateContainer(string messageId, string body)
    {
        var received = new ReceivedMessage
        {
            AckId = Guid.NewGuid().ToString(),
            Message = new PubsubMessage
            {
                MessageId = messageId,
                Data = ByteString.CopyFromUtf8(body)
            }
        };

        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(received);
        return container.Object;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestGetJobsAsync(int batchSize)
    {
        var messageId1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var messageId2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var message1 = CreateContainer(messageId1, data1);
        var message2 = CreateContainer(messageId2, data2);
        var message3 = CreateContainer(Guid.NewGuid().ToString(), data3);
        var message4 = CreateContainer(Guid.NewGuid().ToString(), data4);

        var pubSubMessageSource = new Mock<IGooglePubSubMessageSource>(MockBehavior.Strict);
        pubSubMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message1, message2, message3, message4]);

        var jobSource = new GooglePubSubJobSource(new Mock<IPubSubSubscriberClientSource>().Object,
            pubSubMessageSource.Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreatePassthroughPoisonHandler().Object, NullLogger<GooglePubSubJobSource>.Instance,
            Options.Create(DefaultOptions()));

        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(4, response.Items.Count);

        pubSubMessageSource.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        pubSubMessageSource.Verify(a => a.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken),
            Times.Once);

        Assert.Equal(messageId1, response.Items[0].MessageId);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(messageId2, response.Items[1].MessageId);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal(data3, response.Items[2].Body);
        Assert.Equal(data4, response.Items[3].Body);
        Assert.All(response.Items, item => Assert.IsType<GooglePubSubJobModel>(item));
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var jobSource = new GooglePubSubJobSource(null!, null!,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Mock.Of<IGooglePubSubPoisonMessagesHandler>(),
            NullLogger<GooglePubSubJobSource>.Instance,
            Options.Create(DefaultOptions(20)));

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Empty)]
    public async Task Test_AcknowledgeAsync_IncompatibleMessage(CoreJobResult result)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var source = new Mock<IPubSubSubscriberClientSource>(MockBehavior.Strict);
        var poison = new Mock<IGooglePubSubPoisonMessagesHandler>(MockBehavior.Strict);

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            poison.Object, NullLogger<GooglePubSubJobSource>.Instance, Options.Create(DefaultOptions()));

        var job = new OutsideContextJobModel
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        Assert.Empty(client.Invocations);
        Assert.Empty(source.Invocations);
        Assert.Empty(poison.Invocations);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.InvalidData)]
    public async Task Test_AcknowledgeAsync_NonRecoverable_AcknowledgesWhenNotAlreadyEnforced(
        CoreJobResult result)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var poison = CreatePassthroughPoisonHandler();

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            poison.Object, NullLogger<GooglePubSubJobSource>.Instance, Options.Create(DefaultOptions()));

        await jobSource.AcknowledgeAsync(CreateJob(message.Object), result,
            TestContext.Current.CancellationToken);

        poison.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(message.Object, TestContext.Current.CancellationToken),
            Times.Once);
        client.Verify(c => c.AcknowledgeAsync(message.Object, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.NackAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_RecoverableFailure_NacksWhenNotEnforced(CoreJobResult result)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var poison = CreatePassthroughPoisonHandler();

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            poison.Object, NullLogger<GooglePubSubJobSource>.Instance, Options.Create(DefaultOptions()));

        await jobSource.AcknowledgeAsync(CreateJob(message.Object), result,
            TestContext.Current.CancellationToken);

        poison.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(message.Object, TestContext.Current.CancellationToken),
            Times.Once);
        client.Verify(c => c.NackAsync(message.Object, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.AcknowledgeAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.InvalidData)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_SkipsClientCallsWhenAlreadyEnforced(CoreJobResult result)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var poison = CreatePassthroughPoisonHandler(PoisonEnforcementResult.Enforced);

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            poison.Object, NullLogger<GooglePubSubJobSource>.Instance, Options.Create(DefaultOptions()));

        await jobSource.AcknowledgeAsync(CreateJob(message.Object), result,
            TestContext.Current.CancellationToken);

        poison.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(message.Object, TestContext.Current.CancellationToken),
            Times.Once);
        Assert.Empty(client.Invocations);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Success()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var poison = new Mock<IGooglePubSubPoisonMessagesHandler>(MockBehavior.Strict);

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            poison.Object, NullLogger<GooglePubSubJobSource>.Instance, Options.Create(DefaultOptions()));

        await jobSource.AcknowledgeAsync(CreateJob(message.Object), CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        client.Verify(c => c.AcknowledgeAsync(message.Object, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.NackAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(poison.Invocations);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreatePassthroughPoisonHandler().Object, NullLogger<GooglePubSubJobSource>.Instance,
            Options.Create(DefaultOptions()));

        Assert.Equal(45, jobSource.RecommendedHeartbeatIntervalSeconds);

        await jobSource.HeartbeatAsync(CreateJob(message.Object), TestContext.Current.CancellationToken);

        client.Verify(c => c.ModifyAckDeadlineAsync(message.Object, 60, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync_IncompatibleMessage()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var source = new Mock<IPubSubSubscriberClientSource>(MockBehavior.Strict);

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            CreatePassthroughPoisonHandler().Object, NullLogger<GooglePubSubJobSource>.Instance,
            Options.Create(DefaultOptions(30)));

        var job = new OutsideContextJobModel
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        await jobSource.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        Assert.Empty(client.Invocations);
        Assert.Empty(source.Invocations);
    }

    public class OutsideContextJobModelTests
    {
        [Fact]
        public void ImplementsIRawJobModel()
        {
            var model = new OutsideContextJobModel
            {
                MessageId = Guid.NewGuid().ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                Body = Guid.NewGuid().ToString()
            };

            Assert.IsType<IRawJobModel>(model, false);
        }

        [Fact]
        public void Properties_RoundTripAssignedValues()
        {
            var messageId = Guid.NewGuid().ToString();
            var date = DateTime.UtcNow - TimeSpan.FromDays(1);
            var body = Guid.NewGuid().ToString();

            var model = new OutsideContextJobModel
            {
                MessageId = messageId,
                CreatedAtUtc = date,
                Body = body
            };

            Assert.Equal(messageId, model.MessageId);
            Assert.Equal(messageId, model.IdempotencyId);
            Assert.Equal(date, model.CreatedAtUtc);
            Assert.Equal(body, model.Body);
        }
    }

    /// <summary>
    ///     Stand-in IRawJobModel that is not a <see cref="GooglePubSubJobModel" />, used to exercise
    ///     GooglePubSubJobSource paths that ignore messages from outside this job source.
    /// </summary>
    private class OutsideContextJobModel : IRawJobModel
    {
        public required string MessageId { get; init; }

        // ReSharper disable once ReturnTypeCanBeNotNullable
        public string? IdempotencyId => MessageId;
        public required DateTime CreatedAtUtc { get; init; }
        public required string? Body { get; init; }
    }
}