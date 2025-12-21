using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

internal class AzureQueueStorageJobSource(
    IQueueConsumerClientSource clientSource,
    IAzureQueueStorageMessageSource azureQueueStorageMessageSource,
    ISourceMessageConverter converter,
    ISourceMessageSorter sorter,
    ILogger<AzureQueueStorageJobSource> logger,
    IOptions<AzureQueueStorageConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureJobModel messageAsAzureJobModel)
        {
            // For consideration: Throw some kind of exception?
            return;
        }

        var client = clientSource.GetQueueClient();

        // Azure Queue Storage requires an application-defined mechanism for handling "poison" messages
        // For lack of that, in this template we will treat it like ActiveMQ/RabbitMQ and delete the message.
        // Queueing up failed messages in another DLQ would 
        await client.DeleteMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
    }

    public async Task<JobSourceResponse> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var messages = await azureQueueStorageMessageSource.GetMessagesAsync(cancellationToken);
        var items = new List<IJobModel>();

        foreach (var message in messages)
        {
            try
            {
                logger.LogTrace("Raw Azure Queue Storage message: {MessageBody}", message.Body);

                var @object = converter.Convert(message.Body);
                if (@object is null)
                {
                    continue;
                }

                var data = new AzureJobModel
                {
                    Message = message,
                    Data = @object
                };

                items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Azure Queue Storage message: {MessageBody}", message.Body);
            }
        }

        var response = new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds =
                (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75),
            Items = items.Count > 0 ? sorter.GetSortedListOfJobs(items) : []
        };

        return response;
    }

    public async Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not AzureJobModel messageAsAzureJobModel)
        {
            // For consideration: Throw some kind of exception?
            return;
        }

        var client = clientSource.GetQueueClient();
        await client.SetMessageVisibilityTimeoutAsync(messageAsAzureJobModel.Message,
            TimeSpan.FromSeconds(options.Value.EffectiveVisibilityTimeoutSeconds), cancellationToken);
    }
}