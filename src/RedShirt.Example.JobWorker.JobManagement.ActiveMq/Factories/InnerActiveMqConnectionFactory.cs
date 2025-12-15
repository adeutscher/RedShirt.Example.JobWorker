using Apache.NMS.ActiveMQ;
using Microsoft.Extensions.Logging;
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
    ILogger<InnerActiveMqConnectionFactory> logger)
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

        return new ActiveMqConnectionWrapper(connectionFactory);
    }
}