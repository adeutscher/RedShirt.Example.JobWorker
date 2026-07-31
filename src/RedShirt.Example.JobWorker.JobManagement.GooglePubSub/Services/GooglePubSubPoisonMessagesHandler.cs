using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal interface IGooglePubSubPoisonMessagesHandler
{
    /// <summary>
    ///     When the subscription has no dead-letter topic configured, acknowledge (drop) the message once its
    ///     delivery attempt reaches <see cref="GooglePubSubConfigurationModel.MaximumReceives" />.
    /// </summary>
    /// <returns>
    ///     <c>true</c> if the message was acknowledged as poison; <c>false</c> if no action was taken.
    /// </returns>
    Task<bool> AttemptPoisonMessageEnforcementAsync(IPubSubMessageContainer message,
        CancellationToken cancellationToken = default);
}

internal class GooglePubSubPoisonMessagesHandler(
    IPubSubSubscriberClientSource clientSource,
    IGooglePubSubRetryWrapperService retryWrapperService,
    IOptions<GooglePubSubConfigurationModel> options)
    : IGooglePubSubPoisonMessagesHandler
{
    public async Task<bool> AttemptPoisonMessageEnforcementAsync(IPubSubMessageContainer message,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.DlqNotEnabled)
        {
            // If a dead-letter topic is configured, leave poison message handling to the subscription policy
            return false;
        }

        if ((PubSubMessageAttributeRetriever.TryGetDeliveryAttempt(message) ?? 0) >=
            options.Value.EffectiveMaximumReceives)
        {
            // If the dead-letter topic is not enabled, then attempt to deal with poison messages
            await retryWrapperService.RunAsync(async ct =>
            {
                var client = await clientSource.GetSubscriberClientAsync(ct);
                await client.AcknowledgeAsync(message, ct);
            }, cancellationToken);

            return true;
        }

        return false;
    }
}
