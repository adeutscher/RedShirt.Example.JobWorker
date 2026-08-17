using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Enums;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services.Resilience;
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

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(1, 1)]
    [InlineData(1, 0)] // EffectiveMaximumReceives floors at 1
    public async Task WhenDeliveryAttemptAtOrAboveMaximum_ReturnsEnforced(int deliveryAttempt,
        int maximumReceives)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = maximumReceives
        });

        var message = CreateMessage(deliveryAttempt);

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.Enforced, outcome);
        client.Verify(c => c.AcknowledgeAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    public async Task WhenDeliveryAttemptBelowMaximum_ReturnsNotEnforced(int deliveryAttempt, int maximumReceives)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = maximumReceives
        });

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(CreateMessage(deliveryAttempt),
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.NotEnforced, outcome);
        Assert.Empty(client.Invocations);
    }

    [Fact]
    public async Task WhenDeliveryAttemptMissing_ReturnsNotEnforced()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = 1
        });

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(CreateMessage(0),
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.NotEnforced, outcome);
        Assert.Empty(client.Invocations);
    }

    [Fact]
    public async Task WhenDlqEnabled_ReturnsEnforcementNotEnabled()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>(MockBehavior.Strict);
        var handler = CreateHandler(client, new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = false,
            MaximumReceives = 1
        });

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(CreateMessage(100),
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.EnforcementNotEnabled, outcome);
        Assert.Empty(client.Invocations);
    }
}