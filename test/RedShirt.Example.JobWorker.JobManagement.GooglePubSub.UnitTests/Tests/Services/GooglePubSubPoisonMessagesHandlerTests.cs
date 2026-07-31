using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubPoisonMessagesHandlerTests
{
    private static GooglePubSubPoisonMessagesHandler CreateHandler(
        Mock<IPubSubSubscriberClientWrapper> client,
        GooglePubSubConfigurationModel config)
    {
        var source = new Mock<IPubSubSubscriberClientSource>();
        source.Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        return new GooglePubSubPoisonMessagesHandler(source.Object,
            GooglePubSubRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(config));
    }

    private static IPubSubMessageContainer CreateMessage(int deliveryAttempt)
    {
        var received = new ReceivedMessage
        {
            AckId = Guid.NewGuid().ToString(),
            DeliveryAttempt = deliveryAttempt,
            Message = new PubsubMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Data = ByteString.CopyFromUtf8("{}")
            }
        };

        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(received);
        return container.Object;
    }

    [Fact]
    public async Task WhenDlqEnabled_DoesNotAcknowledge()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60,
            DlqNotEnabled = false,
            MaximumReceives = 1
        });

        var removed = await handler.AttemptPoisonMessageEnforcementAsync(CreateMessage(100),
            TestContext.Current.CancellationToken);

        Assert.False(removed);
        Assert.Empty(client.Invocations);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(1, 1)]
    [InlineData(1, 0)] // EffectiveMaximumReceives floors at 1
    public async Task WhenDeliveryAttemptAtOrAboveMaximum_AcknowledgesMessage(int deliveryAttempt,
        int maximumReceives)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60,
            DlqNotEnabled = true,
            MaximumReceives = maximumReceives
        });

        var message = CreateMessage(deliveryAttempt);

        var removed = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.True(removed);
        client.Verify(c => c.AcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenDeliveryAttemptMissing_DoesNotAcknowledge()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60,
            DlqNotEnabled = true,
            MaximumReceives = 1
        });

        var removed = await handler.AttemptPoisonMessageEnforcementAsync(CreateMessage(0),
            TestContext.Current.CancellationToken);

        Assert.False(removed);
        Assert.Empty(client.Invocations);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    public async Task WhenDeliveryAttemptBelowMaximum_DoesNotAcknowledge(int deliveryAttempt, int maximumReceives)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60,
            DlqNotEnabled = true,
            MaximumReceives = maximumReceives
        });

        var removed = await handler.AttemptPoisonMessageEnforcementAsync(CreateMessage(deliveryAttempt),
            TestContext.Current.CancellationToken);

        Assert.False(removed);
        Assert.Empty(client.Invocations);
    }
}
