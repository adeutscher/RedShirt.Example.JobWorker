using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

internal interface IPubSubSubscriberClientWrapper
{
    Task AcknowledgeAsync(IPubSubMessageContainer messageModel, CancellationToken cancellationToken = default);

    Task<IEnumerable<IPubSubMessageContainer>> GetMessagesAsync(int maxMessages,
        CancellationToken cancellationToken = default);

    Task ModifyAckDeadlineAsync(IPubSubMessageContainer messageModel, int ackDeadlineSeconds,
        CancellationToken cancellationToken = default);

    Task NackAsync(IPubSubMessageContainer messageModel, CancellationToken cancellationToken = default);
}

internal class PubSubSubscriberClientWrapper(
    SubscriberServiceApiClient client,
    SubscriptionName subscriptionName) : IPubSubSubscriberClientWrapper
{
    internal SubscriberServiceApiClient Client => client;

    public Task AcknowledgeAsync(IPubSubMessageContainer messageModel,
        CancellationToken cancellationToken = default)
    {
        return Client.AcknowledgeAsync(subscriptionName, [messageModel.Message!.AckId], cancellationToken);
    }

    public async Task<IEnumerable<IPubSubMessageContainer>> GetMessagesAsync(int maxMessages,
        CancellationToken cancellationToken = default)
    {
        // Bound the pull so an idle subscription does not block the worker poll loop indefinitely.
        var callSettings = CallSettings.FromCancellationToken(cancellationToken)
            .WithExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(1)));

        try
        {
            var response = await Client.PullAsync(subscriptionName, maxMessages: maxMessages,
                callSettings: callSettings);

            return response.ReceivedMessages.Select<ReceivedMessage, IPubSubMessageContainer>(m =>
                new PubSubMessageContainer
                {
                    Message = m
                });
        }
        catch (RpcException e) when (e.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            // Idle pulls commonly surface as deadline exceeded once ReturnImmediately was deprecated.
            return [];
        }
    }

    public Task ModifyAckDeadlineAsync(IPubSubMessageContainer messageModel, int ackDeadlineSeconds,
        CancellationToken cancellationToken = default)
    {
        return Client.ModifyAckDeadlineAsync(subscriptionName, [messageModel.Message!.AckId], ackDeadlineSeconds,
            cancellationToken);
    }

    public Task NackAsync(IPubSubMessageContainer messageModel, CancellationToken cancellationToken = default)
    {
        // Ack deadline of 0 immediately makes the message available for redelivery.
        return ModifyAckDeadlineAsync(messageModel, 0, cancellationToken);
    }

    internal class PubSubMessageContainer : IPubSubMessageContainer
    {
        public required ReceivedMessage? Message { get; init; }
    }
}
