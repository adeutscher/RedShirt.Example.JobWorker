using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

internal interface IQueueConsumerClientSource
{
    Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default);
}

internal class QueueConsumerClientSource(IQueueConsumerClientFactory factory) : IQueueConsumerClientSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IQueueConsumerClientWrapper? _queueClient;

    public async Task<IQueueConsumerClientWrapper> GetQueueClientAsync(CancellationToken cancellationToken = default)
    {
        if (_queueClient is not null)
        {
            return _queueClient;
        }

        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (_queueClient is not null)
            {
                return _queueClient;
            }

            _queueClient = await factory.GetQueueClientAsync(cancellationToken);
            return _queueClient;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}