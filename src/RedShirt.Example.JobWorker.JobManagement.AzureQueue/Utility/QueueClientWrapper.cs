using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

/// <summary>
///     Mockable wrapper around an Azure Queue Storage queue client.
///     I'm very surprised that the Azure package doesn't have interfaces for anything...
/// </summary>
internal interface IQueueConsumerClientWrapper
{
    Task DeleteMessageAsync(IQueueMessageModel message, CancellationToken cancellationToken = default);

    Task<List<IQueueMessageModel>> GetMessagesAsync(int maxMessages, TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);

    Task SetMessageVisibilityTimeoutAsync(IQueueMessageModel message, TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);
}

internal class QueueClientWrapper(QueueClient client) : IQueueConsumerClientWrapper
{
    internal QueueClient QueueClient => client;

    public async Task<List<IQueueMessageModel>> GetMessagesAsync(int maxMessages, TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        var response = await client.ReceiveMessagesAsync(maxMessages, visibilityTimeout, cancellationToken);
        return response is not null
            ? response.Value.Select(IQueueMessageModel (i) => new QueueMessageModel(i)).ToList()
            : [];
    }

    public Task SetMessageVisibilityTimeoutAsync(IQueueMessageModel message, TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        return client.UpdateMessageAsync(message.MessageId, message.PopReceipt, visibilityTimeout: visibilityTimeout,
            cancellationToken: cancellationToken);
    }

    public Task DeleteMessageAsync(IQueueMessageModel message, CancellationToken cancellationToken = default)
    {
        return client.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
    }

    internal sealed class QueueMessageModel(QueueMessage innerMsg) : IQueueMessageModel
    {
        public string Body => innerMsg.Body.ToString();
        public string MessageId => innerMsg.MessageId;
        public string PopReceipt => innerMsg.PopReceipt;
    }
}