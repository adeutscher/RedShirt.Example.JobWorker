using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal class GooglePubSubJobSource(
    IPubSubSubscriberClientSource clientSource,
    IGooglePubSubMessageSource googlePubSubMessageSource,
    ISourceMessageConverter converter,
    IGooglePubSubBodyStringRetriever bodyStringRetriever,
    ILogger<GooglePubSubJobSource> logger,
    IOptions<GooglePubSubConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not GooglePubSubJobModel messageAsPubSubJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetSubscriberClientAsync(cancellationToken);

        if (success)
        {
            await client.AcknowledgeAsync(messageAsPubSubJobModel.Message, cancellationToken);
        }
        else
        {
            /*
             * Failed jobs are nacked (ack deadline set to 0) so they become available for redelivery.
             * Behaviour beyond that defers to the subscription's dead-letter / max-delivery configuration.
             */
            await client.NackAsync(messageAsPubSubJobModel.Message, cancellationToken);
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await googlePubSubMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IJobModel>();

        foreach (var receivedMessage in messages)
        {
            string messageBody;
            try
            {
                messageBody = bodyStringRetriever.GetBody(receivedMessage);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Google Pub/Sub message: {MessageBody}", e.Message);

                /*
                 * Pub/Sub has no per-message dead-letter API on pull; acknowledging removes the poison
                 * message so it cannot keep gumming up the subscription. Prefer configuring a dead-letter
                 * topic on the subscription in production.
                 */
                var client = await clientSource.GetSubscriberClientAsync(cancellationToken);
                await client.AcknowledgeAsync(receivedMessage, cancellationToken);

                continue;
            }

            if (string.IsNullOrWhiteSpace(messageBody))
            {
                var client = await clientSource.GetSubscriberClientAsync(cancellationToken);
                await client.AcknowledgeAsync(receivedMessage, cancellationToken);

                continue;
            }

            try
            {
                logger.LogTrace("Raw Google Pub/Sub message: {MessageBody}", messageBody);

                var @object = converter.Convert(messageBody);
                if (@object is null)
                {
                    var client = await clientSource.GetSubscriberClientAsync(cancellationToken);
                    await client.AcknowledgeAsync(receivedMessage, cancellationToken);

                    continue;
                }

                var data = new GooglePubSubJobModel
                {
                    Message = receivedMessage,
                    CreatedAtUtc = DateTime.UtcNow,
                    Data = @object
                };

                items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Google Pub/Sub message: {MessageBody}", messageBody);
            }
        }

        return new JobSourceResponse
        {
            Items = items
        };
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not GooglePubSubJobModel messageAsPubSubJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetSubscriberClientAsync(cancellationToken);
        await client.ModifyAckDeadlineAsync(messageAsPubSubJobModel.Message,
            options.Value.EffectiveVisibilityTimeoutSeconds, cancellationToken);
    }
}
