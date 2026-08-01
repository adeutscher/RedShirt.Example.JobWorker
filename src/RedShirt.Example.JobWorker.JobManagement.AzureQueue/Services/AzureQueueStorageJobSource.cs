using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

internal class AzureQueueStorageJobSource(
    IQueueConsumerClientSource clientSource,
    IAzureQueueStorageMessageSource azureQueueStorageMessageSource,
    IOptions<AzureQueueStorageConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureQueueStorageRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        if (result.IsRecoverableFailure())
        {
            // Leave the message to expire / become visible again. Azure Queue has no native NAck.
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);

        // Success and unrecoverable (Empty / Parsing / Broken): delete. Azure Queue has no native DLQ.
        // Queueing failed messages in another DLQ would be an application-defined extension.
        await client.DeleteMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await azureQueueStorageMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = messages.Select(IRawJobModel (message) => new AzureQueueStorageRawJobModel
        {
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();

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
        if (message is not AzureQueueStorageRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await client.SetMessageVisibilityTimeoutAsync(messageAsAzureJobModel.Message,
            TimeSpan.FromSeconds(options.Value.EffectiveVisibilityTimeoutSeconds), cancellationToken);
    }
}