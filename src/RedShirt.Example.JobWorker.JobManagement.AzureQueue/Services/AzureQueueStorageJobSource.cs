using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

internal class AzureQueueStorageJobSource(
    IQueueConsumerClientSource clientSource,
    IAzureQueueStorageMessageSource azureQueueStorageMessageSource,
    ILogger<AzureQueueStorageJobSource> logger,
    IOptions<AzureQueueStorageConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeCompletionAsync(IRawJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);

        // Azure Queue Storage requires an application-defined mechanism for handling "poison" messages
        // For lack of that, in this template we will treat it like ActiveMQ/RabbitMQ and delete the message.
        // Queueing up failed messages in another DLQ would 
        await client.DeleteMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await azureQueueStorageMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IRawJobModel>();

        foreach (var message in messages)
        {
            logger.LogTrace("Raw Azure Queue Storage message: {MessageBody}", message.Body);

            var data = new AzureJobModel
            {
                Message = message,
                CreatedAtUtc = DateTime.UtcNow,
                Body = message.Body
            };

            items.Add(data);
        }

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
        if (message is not AzureJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await client.SetMessageVisibilityTimeoutAsync(messageAsAzureJobModel.Message,
            TimeSpan.FromSeconds(options.Value.EffectiveVisibilityTimeoutSeconds), cancellationToken);
    }
}