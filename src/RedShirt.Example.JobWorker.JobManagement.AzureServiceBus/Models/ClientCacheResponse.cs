namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal sealed class ClientCacheResponse<T>
{
    public required bool CachedClient { get; init; }
    public required T Client { get; init; }
}