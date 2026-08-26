using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;

internal interface IInnerRabbitMqConnectionFactory
{
    Task<IRabbitConnectionWrapper> GetConnectionFactoryWrapperAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class InnerRabbitMqConnectionFactory(
    IRabbitMqServerConfigurationSource configurationSource,
    IRabbitMqSubscribeConfigurationService rabbitMqSubscribeConfigurationService)
    : IInnerRabbitMqConnectionFactory
{
    public async Task<IRabbitConnectionWrapper> GetConnectionFactoryWrapperAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationSource.GetConfigurationAsync(
            forceNewSecretManagerPull,
            cancellationToken);

        // Subscription mode uses our own reconnect/resubscribe loop (see RabbitMqSubscribeJobSource),
        // which can force a fresh secret-manager pull when credentials change on top of a connection interruption.
        // RabbitMQ client AutomaticRecovery would reconnect with the original factory credentials and fight that.
        var automaticRecoveryEnabled = !rabbitMqSubscribeConfigurationService.IsSubscription;

        var connectionFactory = new ConnectionFactory
        {
            UserName = configuration.User,
            Password = configuration.Password,
            HostName = configuration.Hostname,
            VirtualHost = configuration.VirtualHost,
            TopologyRecoveryEnabled = false,
            AutomaticRecoveryEnabled = automaticRecoveryEnabled,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            // Best practice range: 5 to 20 seconds
            RequestedHeartbeat = TimeSpan.FromSeconds(15)
        };

        if (automaticRecoveryEnabled)
        {
            connectionFactory.NetworkRecoveryInterval = TimeSpan.FromSeconds(1);
        }

        return new RabbitConnectionWrapper(connectionFactory);
    }
}