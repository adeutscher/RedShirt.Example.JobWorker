using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;

internal interface IInnerRabbitMqConnectionFactory
{
    Task<IRabbitConnectionWrapper> GetConnectionFactoryWrapperAsync(
        CancellationToken cancellationToken = default);
}

internal class InnerRabbitMqConnectionFactory(IRabbitMqServerConfigurationSource configurationSource)
    : IInnerRabbitMqConnectionFactory
{
    public async Task<IRabbitConnectionWrapper> GetConnectionFactoryWrapperAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationSource.GetConfigurationAsync(cancellationToken);
        var connectionFactory = new ConnectionFactory
        {
            UserName = configuration.User,
            Password = configuration.Password,
            HostName = configuration.Hostname,
            VirtualHost = configuration.VirtualHost,
            TopologyRecoveryEnabled = false,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(1),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            // Best practice range: 5 to 20 seconds
            RequestedHeartbeat = TimeSpan.FromSeconds(15)
        };

        return new RabbitConnectionWrapper(connectionFactory);
    }
}