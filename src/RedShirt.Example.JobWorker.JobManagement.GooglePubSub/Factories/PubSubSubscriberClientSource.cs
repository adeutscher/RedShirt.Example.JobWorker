using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;

internal interface IPubSubSubscriberClientSource
{
    Task<IPubSubSubscriberClientWrapper> GetSubscriberClientAsync(CancellationToken cancellationToken = default);
}

internal class PubSubSubscriberClientSource(IPubSubSubscriberClientFactory factory) : IPubSubSubscriberClientSource
{
    private readonly Lazy<Task<IPubSubSubscriberClientWrapper>> _subscriberClient =
        new(() => factory.GetSubscriberClientAsync());

    public Task<IPubSubSubscriberClientWrapper> GetSubscriberClientAsync(
        CancellationToken cancellationToken = default)
    {
        return _subscriberClient.Value;
    }
}
