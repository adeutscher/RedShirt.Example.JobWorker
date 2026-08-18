using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Factories;

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
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = false,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(1),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        return new RabbitConnectionWrapper(connectionFactory);
    }
}