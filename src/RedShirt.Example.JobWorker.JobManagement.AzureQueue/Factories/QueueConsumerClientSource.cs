using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

internal interface IQueueConsumerClientSource
{
    Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default);
}

internal class QueueConsumerClientSource(IQueueConsumerClientFactory factory) : IQueueConsumerClientSource
{
    private readonly Lazy<Task<IQueueConsumerClientWrapper>> _queueClient = new(() => factory.GetQueueClientAsync());

    public Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default)
    {
        return _queueClient.Value;
    }
}