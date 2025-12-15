using Apache.NMS;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;

internal interface IActiveMqConnectionFactory
{
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
}

internal class ActiveMqConnectionFactory(IInnerActiveMqConnectionFactory innerActiveMqConnectionFactory)
    : IActiveMqConnectionFactory
{
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        var wrapper = await innerActiveMqConnectionFactory.GetConnectionFactoryWrapperAsync(cancellationToken);

        return await wrapper.CreateConnectionAsync(cancellationToken);
    }
}