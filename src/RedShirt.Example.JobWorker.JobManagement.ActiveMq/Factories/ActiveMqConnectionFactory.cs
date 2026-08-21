using Apache.NMS;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;

internal interface IActiveMqConnectionFactory
{
    Task<IConnection> GetConnectionAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class ActiveMqConnectionFactory(IInnerActiveMqConnectionFactory innerActiveMqConnectionFactory)
    : IActiveMqConnectionFactory
{
    public async Task<IConnection> GetConnectionAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var wrapper = await innerActiveMqConnectionFactory.GetConnectionFactoryWrapperAsync(
            forceNewSecretManagerPull,
            cancellationToken);

        return await wrapper.CreateConnectionAsync(cancellationToken);
    }
}