using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

internal interface IAzureQueueStorageMessageSource
{
    Task<List<IQueueMessageModel>> GetMessagesAsync(CancellationToken cancellationToken = default);
}

internal class AzureQueueStorageMessageSource(
    IQueueConsumerClientSource clientSource,
    IOptions<AzureQueueStorageConfigurationModel> options) : IAzureQueueStorageMessageSource
{
    private const int MaxBatchSizePerRequest = 32;

    private async Task<List<IQueueMessageModel>> GetAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var client = clientSource.GetQueueClient();
        return await client.GetMessagesAsync(batchSize,
            TimeSpan.FromSeconds(options.Value.EffectiveVisibilityTimeoutSeconds), cancellationToken);
    }

    public async Task<List<IQueueMessageModel>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        var messages = new List<IQueueMessageModel>();
        var batchSize = options.Value.EffectiveBatchSize;

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