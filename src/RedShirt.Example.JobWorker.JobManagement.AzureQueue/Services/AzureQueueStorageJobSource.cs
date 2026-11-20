using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;

internal class AzureQueueStorageJobSource(
    IQueueConsumerClientSource clientSource,
    IAzureQueueStorageMessageSource azureQueueStorageMessageSource,
    IAzureQueueStorageRetryWrapperService retryWrapperService,
    IOptions<AzureQueueStorageConfigurationModel> options,
    ILogger<AzureQueueStorageJobSource> logger) : IJobSource
{
    private async Task DeleteAsync(IQueueMessageModel message, CancellationToken cancellationToken)
    {
        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await retryWrapperService.RunAsync(async ct => { await client.DeleteMessageAsync(message, ct); },
            cancellationToken);
    }

    /// <summary>
    ///     Delete a message that was already received more than the allowed number of times without being acknowledged,
    ///     for example because it crashed the worker. Returns true if the message should not be run.
    /// </summary>
    private async Task<bool> TryDiscardExhaustedMessageAsync(IQueueMessageModel message,
        CancellationToken cancellationToken)
    {
        var maximumReceives = options.Value.EffectiveMaximumReceives;
        if (message.DequeueCount <= maximumReceives)
        {
            return false;
        }

        logger.LogWarning(
            "Azure Queue message {MessageId} was received {DequeueCount} times, more than the maximum of {MaximumReceives}, without being acknowledged. Deleting it without running it.",
            message.MessageId, message.DequeueCount, maximumReceives);

        try
        {
            await DeleteAsync(message, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Not fatal for the rest of the batch. The message becomes visible again and is discarded on a later receive.
            logger.LogError(e, "Could not delete Azure Queue message {MessageId}", message.MessageId);
        }

        return true;
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureQueueStorageRawJobModel messageAsAzureJobModel)
        {
            return;
        }

        if (result.IsRecoverableFailure())
        {
            var maximumReceives = options.Value.EffectiveMaximumReceives;
            if (messageAsAzureJobModel.Message.DequeueCount < maximumReceives)
            {
                // Leave the message to expire / become visible again and naturally falling back into the queue.
                // Azure Queue has no native NAck.
                return;
            }

            // Out of receives. Azure Queue has no native DLQ, so the message is deleted rather than looping forever.
            logger.LogWarning(
                "Azure Queue message {MessageId} failed on receive {DequeueCount} of {MaximumReceives}. Deleting it.",
                messageAsAzureJobModel.MessageId, messageAsAzureJobModel.Message.DequeueCount, maximumReceives);
        }

        // Success and unrecoverable (Empty / Parsing / InvalidData): delete. Azure Queue has no native DLQ.
        // Queueing failed messages in another DLQ would be an application-defined extension.
        await DeleteAsync(messageAsAzureJobModel.Message, cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await azureQueueStorageMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IRawJobModel>();

        foreach (var message in messages)
        {
            if (await TryDiscardExhaustedMessageAsync(message, cancellationToken))
            {
                continue;
            }

            items.Add(new AzureQueueStorageRawJobModel
            {
                Message = message,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var response = new JobSourceResponse
        {
            Items = items
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public bool IsSubscriptionSource => false;

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not AzureQueueStorageRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await retryWrapperService.RunAsync(async ct =>
        {
            await client.SetMessageVisibilityTimeoutAsync(messageAsAzureJobModel.Message,
                TimeSpan.FromSeconds(options.Value.EffectiveVisibilityTimeoutSeconds), ct);
        }, cancellationToken);
    }

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}