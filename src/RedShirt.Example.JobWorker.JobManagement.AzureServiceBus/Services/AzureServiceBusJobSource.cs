using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal class AzureServiceBusJobSource(
    IBusReceiverClientSource clientSource,
    IAzureServiceBusMessageSource azureServiceBusServiceSource,
    IOptions<AzureServiceBusConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeCompletionAsync(IRawJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);

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

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messagesFromSource = await azureServiceBusServiceSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = messagesFromSource
            .Select(receivedMessage => new AzureRawJobModel
            {
                Message = receivedMessage,
                CreatedAtUtc = DateTime.UtcNow
            } as IRawJobModel).ToList();

        var response = new JobSourceResponse
        {
            Items = items
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await client.RenewMessageLockAsync(messageAsAzureJobModel.Message, cancellationToken);
    }
}