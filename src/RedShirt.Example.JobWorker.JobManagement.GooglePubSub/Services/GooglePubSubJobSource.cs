using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Enums;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal class GooglePubSubJobSource(
    IPubSubSubscriberClientSource clientSource,
    IGooglePubSubMessageSource googlePubSubMessageSource,
    IGooglePubSubRetryWrapperService retryWrapperService,
    IGooglePubSubPoisonMessagesHandler poisonMessagesHandler,
    IOptions<GooglePubSubConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not GooglePubSubJobModel messageAsPubSubJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        if (result.IsSuccessful())
        {
            await AcknowledgeMessageAsync(messageAsPubSubJobModel.Message, cancellationToken);
            return;
        }

        // Whether the failed message was recoverable or not,
        //  the Pub/Sub implementation's reaction is to attempt to enforce
        //  a consumer-based poison-handling system
        var poisonEnforcementResult =
            await poisonMessagesHandler.AttemptPoisonMessageEnforcementAsync(messageAsPubSubJobModel.Message,
                cancellationToken);

        if (poisonEnforcementResult == PoisonEnforcementResult.Enforced)
        {
            return;
        }

        if (!result.IsRecoverableFailure())
        {
            // The message was not already acknowledged by consumer-based poison message handling
            // and the message is not recoverable, so the message should be acknowledged away.
            await AcknowledgeMessageAsync(messageAsPubSubJobModel.Message, cancellationToken);
            return;
        }

        /*
         * Recoverable execution failures: nack (ack deadline set to 0) so the message becomes available again /
         * counts toward the subscription's configured max-delivery / dead-letter policy when present.
         */
        await retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetSubscriberClientAsync(ct);
            await client.NackAsync(messageAsPubSubJobModel.Message, ct);
        }, cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messagesFromSource = await googlePubSubMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = messagesFromSource
            .Select(receivedMessage => new GooglePubSubJobModel
            {
                Message = receivedMessage,
                CreatedAtUtc = DateTime.UtcNow
            } as IRawJobModel).ToList();

        return new JobSourceResponse
        {
            Items = items
        };
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not GooglePubSubJobModel messageAsPubSubJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        await retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetSubscriberClientAsync(ct);
            await client.ModifyAckDeadlineAsync(messageAsPubSubJobModel.Message,
                options.Value.EffectiveVisibilityTimeoutSeconds, ct);
        }, cancellationToken);
    }

    private Task AcknowledgeMessageAsync(IPubSubMessageContainer message, CancellationToken cancellationToken) =>
        retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetSubscriberClientAsync(ct);
            await client.AcknowledgeAsync(message, ct);
        }, cancellationToken);
}
