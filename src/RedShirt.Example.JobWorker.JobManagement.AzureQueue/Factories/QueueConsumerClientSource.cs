using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;

internal interface IQueueConsumerClientSource
{
    IQueueConsumerClientWrapper GetQueueClient();
}

internal class QueueConsumerClientSource(IQueueConsumerClientFactory factory) : IQueueConsumerClientSource
{
    private readonly Lazy<IQueueConsumerClientWrapper> _queueClient = new(factory.GetQueueClient);

    public IQueueConsumerClientWrapper GetQueueClient()
    {
        return _queueClient.Value;
    }
}