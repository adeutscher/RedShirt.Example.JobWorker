using Apache.NMS.ActiveMQ;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;

internal interface IInnerActiveMqConnectionFactory
{
    Task<IActiveConnectionWrapper> GetConnectionFactoryWrapperAsync(
        CancellationToken cancellationToken = default);
}

internal class InnerActiveMqConnectionFactory(
    IActiveMqServerConfigurationSource configurationSource,
    IActiveMqSubscribeConfigurationService activeMqSubscribeConfigurationService,
    ICoreConfigurationService coreConfigurationService)
    : IInnerActiveMqConnectionFactory
{
    public async Task<IActiveConnectionWrapper> GetConnectionFactoryWrapperAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationSource.GetConfigurationAsync(cancellationToken);
        var connectionFactory = new ConnectionFactory(configuration.BrokerUri)
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
}