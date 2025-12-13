using RabbitMQ.Client;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Wrappers;

internal interface IRabbitConnectionWrapper
{
    IConnectionFactory ConnectionFactory { get; }
    Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

internal class RabbitConnectionWrapper(ConnectionFactory connectionFactory) : IRabbitConnectionWrapper
{
    /// <summary>
    ///     Concession to unit tests
    /// </summary>
    internal ConnectionFactory InternalConnectionFactory => connectionFactory;

    public IConnectionFactory ConnectionFactory => connectionFactory;

    public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        return connectionFactory.CreateConnectionAsync(cancellationToken);
    }
}