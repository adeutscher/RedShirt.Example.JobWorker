using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

internal interface IAzureQueueStorageMessageSource
{
    Task<List<IQueueMessageModel>> GetMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}

internal class AzureQueueStorageMessageSource(
    IQueueConsumerClientSource clientSource,
    IAzureQueueStorageRetryWrapperService retryWrapperService,
    IOptions<AzureQueueStorageConfigurationModel> options) : IAzureQueueStorageMessageSource
{
    /// <summary>
    ///     Azure Queue Storage allows a client to receive up to 32 messages from a queue in a single operation.
    ///     Source: https://learn.microsoft.com/en-us/azure/storage/queues/storage-performance-checklist
    /// </summary>
    private const int MaxBatchSizePerRequest = 32;

    private Task<List<IQueueMessageModel>> GetAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetQueueClientAsync(ct);
            return await client.GetMessagesAsync(batchSize,
                TimeSpan.FromSeconds(options.Value.EffectiveVisibilityTimeoutSeconds), ct);
        }, cancellationToken);
    }

    public async Task<List<IQueueMessageModel>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<IQueueMessageModel>();

        while (batchSize > MaxBatchSizePerRequest)
        {
            var loopResult = await GetAsync(Math.Min(batchSize, MaxBatchSizePerRequest), cancellationToken);

            messages.AddRange(loopResult);

            if (loopResult.Count < MaxBatchSizePerRequest)
            {
                // Received less than our batch size
                break;
            }

            batchSize -= MaxBatchSizePerRequest;
        }

        if (batchSize is > 0 and <= MaxBatchSizePerRequest)
        {
            messages.AddRange(await GetAsync(batchSize, cancellationToken));
        }

        return messages;
    }
}