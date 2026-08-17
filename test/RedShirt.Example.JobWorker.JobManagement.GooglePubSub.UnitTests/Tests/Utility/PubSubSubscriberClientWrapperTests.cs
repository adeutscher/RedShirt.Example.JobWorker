using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Utility;

public class PubSubSubscriberClientWrapperTests
{
    private const int DefaultWaitTimeSeconds = 0;

    private static readonly SubscriptionName Subscription =
        SubscriptionName.FromProjectSubscription("local-pubsub", "jobs-subscription");

    private static ReceivedMessage CreateReceivedMessage(string ackId = "ack-1")
    {
        return new ReceivedMessage
        {
            AckId = ackId,
            Message = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8("payload"),
                MessageId = "mid-1"
            }
        };
    }

    private static IPubSubMessageContainer CreateContainer(string ackId = "ack-1")
    {
        return new PubSubSubscriberClientWrapper.PubSubMessageContainer
        {
            Message = CreateReceivedMessage(ackId)
        };
    }

    private static bool MatchesCallSettings(CallSettings settings, int waitTimeSeconds,
        CancellationToken cancellationToken)
    {
        var expectedWaitTimeSpan = TimeSpan.FromSeconds(waitTimeSeconds + 1); // Account for padding hardcoded in client

        return settings.CancellationToken == cancellationToken
               && settings.Expiration is not null
               && settings.Expiration.Timeout == expectedWaitTimeSpan;
    }

    [Fact]
    public async Task AcknowledgeAsync_ForwardsAckIdToClient()
    {
        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.AcknowledgeAsync(
                Subscription,
                It.Is<IEnumerable<string>>(ids => ids.Single() == "ack-1"),
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        await wrapper.AcknowledgeAsync(CreateContainer(), TestContext.Current.CancellationToken);

        client.Verify();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task GetMessagesAsync_DeadlineExceeded_ReturnsEmpty(int waitTimeSeconds)
    {
        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.PullAsync(
                Subscription,
                5,
                It.Is<CallSettings>(cs =>
                    MatchesCallSettings(cs, waitTimeSeconds, TestContext.Current.CancellationToken))))
            .ThrowsAsync(new RpcException(new Status(StatusCode.DeadlineExceeded, "idle pull")))
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        var messages = await wrapper.GetMessagesAsync(5, waitTimeSeconds, TestContext.Current.CancellationToken);

        Assert.Empty(messages);
        client.Verify();
    }

    [Fact]
    public async Task GetMessagesAsync_MapsReceivedMessagesToContainers()
    {
        var pullResponse = new PullResponse();
        pullResponse.ReceivedMessages.Add(CreateReceivedMessage("ack-a"));
        pullResponse.ReceivedMessages.Add(CreateReceivedMessage("ack-b"));

        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.PullAsync(
                Subscription,
                10,
                It.Is<CallSettings>(cs =>
                    MatchesCallSettings(cs, DefaultWaitTimeSeconds, TestContext.Current.CancellationToken))))
            .ReturnsAsync(pullResponse)
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        var messages = (await wrapper.GetMessagesAsync(10, DefaultWaitTimeSeconds,
            TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, messages.Count);
        Assert.Equal("ack-a", messages[0].Message!.AckId);
        Assert.Equal("ack-b", messages[1].Message!.AckId);
        client.Verify();
    }

    [Fact]
    public async Task GetMessagesAsync_Unavailable_Propagates()
    {
        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.PullAsync(Subscription, 5, It.IsAny<CallSettings>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")))
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            wrapper.GetMessagesAsync(5, DefaultWaitTimeSeconds, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        client.Verify();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task GetMessagesAsync_UsesConfiguredExpirationAndCancellationToken(int waitTimeSeconds)
    {
        var pullResponse = new PullResponse();
        pullResponse.ReceivedMessages.Add(CreateReceivedMessage("ack-a"));

        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.PullAsync(
                Subscription,
                10,
                It.Is<CallSettings>(cs =>
                    MatchesCallSettings(cs, waitTimeSeconds, TestContext.Current.CancellationToken))))
            .ReturnsAsync(pullResponse)
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        var messages = (await wrapper.GetMessagesAsync(10, waitTimeSeconds,
            TestContext.Current.CancellationToken)).ToList();

        Assert.Single(messages);
        Assert.Equal("ack-a", messages[0].Message!.AckId);
        client.Verify();
    }

    [Fact]
    public async Task ModifyAckDeadlineAsync_ForwardsDeadlineToClient()
    {
        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.ModifyAckDeadlineAsync(
                Subscription,
                It.Is<IEnumerable<string>>(ids => ids.Single() == "ack-1"),
                45,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        await wrapper.ModifyAckDeadlineAsync(CreateContainer(), 45, TestContext.Current.CancellationToken);

        client.Verify();
    }

    [Fact]
    public async Task NackAsync_SetsAckDeadlineToZero()
    {
        var client = new Mock<SubscriberServiceApiClient>(MockBehavior.Strict);
        client
            .Setup(c => c.ModifyAckDeadlineAsync(
                Subscription,
                It.Is<IEnumerable<string>>(ids => ids.Single() == "ack-1"),
                0,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var wrapper = new PubSubSubscriberClientWrapper(client.Object, Subscription);

        await wrapper.NackAsync(CreateContainer(), TestContext.Current.CancellationToken);

        client.Verify();
    }
}