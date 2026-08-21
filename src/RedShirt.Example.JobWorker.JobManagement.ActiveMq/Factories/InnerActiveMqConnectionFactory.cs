using Apache.NMS.ActiveMQ;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;

internal interface IInnerActiveMqConnectionFactory
{
    Task<IActiveConnectionWrapper> GetConnectionFactoryWrapperAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class InnerActiveMqConnectionFactory(
    IActiveMqServerConfigurationSource configurationSource,
    IActiveMqSubscribeConfigurationService activeMqSubscribeConfigurationService,
    ICoreConfigurationService coreConfigurationService)
    : IInnerActiveMqConnectionFactory
{
    public async Task<IActiveConnectionWrapper> GetConnectionFactoryWrapperAsync(
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationSource.GetConfigurationAsync(
            forceNewSecretManagerPull,
            cancellationToken);

        // If we are using a subscription, then enrich the URI to ensure fail-over
        var brokerUri = activeMqSubscribeConfigurationService.IsSubscription
            ? EnsureFailoverUri(configuration.BrokerUri)
            : configuration.BrokerUri;

        var connectionFactory = new ConnectionFactory(brokerUri)
        {
            UserName = configuration.User,
            Password = configuration.Password
        };

        if (activeMqSubscribeConfigurationService.IsSubscription)
        {
            // Cap unacked messages pushed to the consumer (listener / receive), analogous to RabbitMQ BasicQos.
            // Only do this for subscriptions, as it's not guaranteed that a user
            //  would set a backlog size for batch-mode polling and I don't want to worry about the weird interaction.
            connectionFactory.PrefetchPolicy.QueuePrefetch = coreConfigurationService.GetBacklogSize();
        }

        return new ActiveMqConnectionWrapper(connectionFactory);
    }

    /// <summary>
    ///     Wraps a plain broker URI in the NMS failover transport so the client reconnects
    ///     after network interruptions (analogous to RabbitMQ AutomaticRecoveryEnabled).
    ///     URIs that already use <c>failover:</c> are left unchanged.
    /// </summary>
    internal static string EnsureFailoverUri(string brokerUri)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (brokerUri.StartsWith("failover:", StringComparison.OrdinalIgnoreCase))
        {
            return brokerUri;
        }

        // initialReconnectDelay=1000 mirrors RabbitMQ NetworkRecoveryInterval of 1s.
        return
            $"failover:({brokerUri})?transport.initialReconnectDelay=1000&transport.maxReconnectDelay=30000&transport.useExponentialBackOff=true";
    }
}