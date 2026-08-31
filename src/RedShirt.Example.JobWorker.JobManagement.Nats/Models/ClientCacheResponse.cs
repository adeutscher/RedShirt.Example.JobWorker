namespace RedShirt.Example.JobWorker.JobManagement.Nats.Models;

internal sealed class ClientCacheResponse<TClient>
{
    public required bool CachedClient { get; init; }
    public required TClient Client { get; init; }
}