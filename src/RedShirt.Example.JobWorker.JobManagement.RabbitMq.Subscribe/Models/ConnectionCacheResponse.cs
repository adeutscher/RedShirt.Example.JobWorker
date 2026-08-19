using RabbitMQ.Client;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;

internal interface IConnectionCacheResponse
{
    bool CachedConnection { get; }
    IConnection Connection { get; }
}

public class ConnectionCacheResponse : IConnectionCacheResponse
{
    public required bool CachedConnection { get; init; }
    public required IConnection Connection { get; init; }
}