using Apache.NMS;
using Apache.NMS.ActiveMQ;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

internal interface IActiveConnectionWrapper
{
    IConnectionFactory ConnectionFactory { get; }
    Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

internal class ActiveMqConnectionWrapper(IConnectionFactory connectionFactory) : IActiveConnectionWrapper
{
    /// <summary>
    ///     Concession to unit tests
    /// </summary>
    internal ConnectionFactory InternalConnectionFactory => (connectionFactory as ConnectionFactory)!;

    public IConnectionFactory ConnectionFactory => connectionFactory;

    public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        return connectionFactory.CreateConnectionAsync();
    }
}