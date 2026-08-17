using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

internal interface IPubSubSubscriberClientWrapper
{
    Task AcknowledgeAsync(IPubSubMessageContainer messageModel, CancellationToken cancellationToken = default);

    Task<IEnumerable<IPubSubMessageContainer>> GetMessagesAsync(int maxMessages, int waitTimeSeconds,
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

    public async Task<IEnumerable<IPubSubMessageContainer>> GetMessagesAsync(int maxMessages, int waitTimeSeconds,
        CancellationToken cancellationToken = default)
    {
        var callSettings = CallSettings.FromCancellationToken(cancellationToken);
        if (waitTimeSeconds > 0)
        {
            // Long-poll for up to the requested wait time.
            callSettings = callSettings
                .WithExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(waitTimeSeconds)));
        }

        try
        {
            var response = await Client.PullAsync(subscriptionName, maxMessages,
                callSettings);

            return response.ReceivedMessages.Select<ReceivedMessage, IPubSubMessageContainer>(m =>
                new PubSubMessageContainer
                {
                    Message = m
                });
        }
        catch (RpcException e) when (e.StatusCode is StatusCode.DeadlineExceeded)
        {
            // Idle pulls commonly surface as deadline exceeded once ReturnImmediately was deprecated.
            // Unavailable and other transport failures are left for the retry/arbiter layer.
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