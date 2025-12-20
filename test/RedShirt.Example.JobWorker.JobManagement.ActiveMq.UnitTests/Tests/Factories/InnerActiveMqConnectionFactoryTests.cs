using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Factories;

public class InnerActiveMqConnectionFactoryTests
{
    [Fact]
    public async Task Test_GetWrapperAsync()
    {
        var valueName = Guid.NewGuid().ToString();
        var valuePassword = Guid.NewGuid().ToString();
        // The factory constructor insists that the URI be properly formatted.
        // ToStringing the URI also tacks a '/' onto the end. Go figure.
        var valueHostname = "tcp://localhost:1234/";

        var configSource = new Mock<IActiveMqServerConfigurationSource>(MockBehavior.Strict);
        configSource.Setup(cs => cs.GetConfigurationAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(new ActiveMqServerConfigurationModel
            {
                BrokerUri = valueHostname,
                User = valueName,
                Password = valuePassword
            });

        var innerFactory = new InnerActiveMqConnectionFactory(configSource.Object);

        var rawWrapper = await innerFactory.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(rawWrapper);
        var wrapper = rawWrapper as ActiveMqConnectionWrapper;
        Assert.NotNull(wrapper);
        Assert.Equal(valueName, wrapper.InternalConnectionFactory.UserName);
        Assert.Equal(valuePassword, wrapper.InternalConnectionFactory.Password);
        Assert.Equal(valueHostname, wrapper.InternalConnectionFactory.BrokerUri.ToString());
        Assert.Same(wrapper.InternalConnectionFactory, wrapper.ConnectionFactory);
    }
}