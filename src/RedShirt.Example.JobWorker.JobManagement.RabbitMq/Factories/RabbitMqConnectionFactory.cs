using RabbitMQ.Client;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;

internal interface IRabbitMqConnectionFactory
{
    Task<IConnection> GetConnectionAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class RabbitMqConnectionFactory(IInnerRabbitMqConnectionFactory innerRabbitMqConnectionFactory)
    : IRabbitMqConnectionFactory
{
    public async Task<IConnection> GetConnectionAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var wrapper = await innerRabbitMqConnectionFactory.GetConnectionFactoryWrapperAsync(
            forceNewSecretManagerPull,
            cancellationToken);

        return await wrapper.CreateConnectionAsync(cancellationToken);
    }
}