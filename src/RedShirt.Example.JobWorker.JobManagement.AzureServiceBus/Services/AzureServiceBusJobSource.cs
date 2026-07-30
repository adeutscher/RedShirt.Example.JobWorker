using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal class AzureServiceBusJobSource(
    IBusReceiverClientSource clientSource,
    IAzureServiceBusMessageSource azureServiceBusServiceSource,
    ISourceMessageConverter converter,
    IAzureServiceBusBodyStringRetriever bodyStringRetriever,
    ILogger<AzureServiceBusJobSource> logger,
    IOptions<AzureServiceBusConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync();

        if (success)
        {
            await client.CompleteMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
        }
        else
        {
            /*
             * This template will treat failed Azure Service Bus jobs similar to the strategy used for SQS.
             * Messages will not be completed, and behaviour will defer to the service bus queue's configured maximum delivery count.
             * Using the service bus client's AbandonMessageAsync method, but one could choose to adjust this template to
             * just let the message time out and fall back into the queue if desired.
             */
            await client.AbandonMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await azureServiceBusServiceSource.GetMessagesAsync(batchSize, cancellationToken);
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
                logger.LogWarning(e, "Error parsing Azure Service Bus message: {MessageBody}", e.Message);

                /*
                 * What exactly to do with bad messages is a bit up in the air at the moment.
                 * Marking them for the dead-letter queue is 'good enough' for now in this general template.
                 */

                // Send the message to the dead-letter so that it cannot keep gumming up the message bus
                var client = await clientSource.GetQueueClientAsync(cancellationToken);
                await client.DeadLetterMessageAsync(receivedMessage, "Body parsing error",
                    e.Message + " " + e.StackTrace, cancellationToken);

                // Proceed to the next message
                continue;
            }

            if (string.IsNullOrWhiteSpace(messageBody))
            {
                // Avoiding unhandled hung message

                // Send the message to the dead-letter so that it cannot keep gumming up the message bus
                var client = await clientSource.GetQueueClientAsync(cancellationToken);
                await client.DeadLetterMessageAsync(receivedMessage, "Empty body",
                    "Empty body", cancellationToken);

                // Proceed to next message
                continue;
            }

            try
            {
                logger.LogTrace("Raw Azure Service Bus message: {MessageBody}", messageBody);

                var @object = converter.Convert(messageBody);
                if (@object is null)
                {
                    // Assume a deterministic conversion failure
                    var client = await clientSource.GetQueueClientAsync(cancellationToken);
                    await client.DeadLetterMessageAsync(receivedMessage, "Conversion failure",
                        "Conversion failure", cancellationToken);

                    // Proceed to next message
                    continue;
                }

                var data = new AzureJobModel
                {
                    Message = receivedMessage,
                    CreatedAtUtc = DateTime.UtcNow,
                    Data = @object
                };

                items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Azure Service Bus message: {MessageBody}", messageBody);
            }
        }

        var response = new JobSourceResponse
        {
            Items = items
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not AzureJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await client.RenewMessageLockAsync(messageAsAzureJobModel.Message, cancellationToken);
    }
}