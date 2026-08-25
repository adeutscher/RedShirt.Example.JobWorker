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

        // Subscription mode uses our own reconnect/resubscribe loop (see ActiveMqSubscribeJobSource),
        // which can force a fresh secret-manager pull when credentials change on top of a connection interruption.
        // NMS failover transport would reconnect with the original factory credentials and fight that mechanism — strip it.
        var brokerUri = activeMqSubscribeConfigurationService.IsSubscription
            ? StripFailoverUri(configuration.BrokerUri)
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
            connectionFactory.PrefetchPolicy.QueuePrefetch = coreConfigurationService.GetFetchCount();
        }

        return new ActiveMqConnectionWrapper(connectionFactory);
    }

    /// <summary>
    ///     Removes an NMS <c>failover:</c> wrapper from <paramref name="brokerUri" />, leaving the nested
    ///     broker address (first composite URI when several are listed). Plain URIs are returned unchanged.
    /// </summary>
    internal static string StripFailoverUri(string brokerUri)
    {
        if (!brokerUri.StartsWith("failover:", StringComparison.OrdinalIgnoreCase))
        {
            return brokerUri;
        }

        var open = brokerUri.IndexOf('(');
        var close = brokerUri.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return brokerUri;
        }

        var nested = brokerUri[(open + 1)..close];
        var comma = nested.IndexOf(',');
        return comma < 0 ? nested : nested[..comma];
    }
}