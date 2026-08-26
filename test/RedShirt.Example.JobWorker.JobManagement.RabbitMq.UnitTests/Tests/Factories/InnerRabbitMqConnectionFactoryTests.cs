using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Factories;

public class InnerRabbitMqConnectionFactoryTests
{
    [Fact]
    public async Task GetWrapperAsync_WhenSubscription_DisablesAutomaticRecovery()
    {
        var configSource = new Mock<IRabbitMqServerConfigurationSource>(MockBehavior.Strict);
        configSource.Setup(cs => cs.GetConfigurationAsync(false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new RabbitMqServerConfigurationModel
            {
                Hostname = "localhost",
                VirtualHost = "/",
                User = "guest",
                Password = "guest"
            });

        var subscribeConfig = new Mock<IRabbitMqSubscribeConfigurationService>(MockBehavior.Strict);
        subscribeConfig.SetupGet(c => c.IsSubscription).Returns(true);

        var innerFactory = new InnerRabbitMqConnectionFactory(configSource.Object, subscribeConfig.Object);
        var rawWrapper =
            await innerFactory.GetConnectionFactoryWrapperAsync(false, TestContext.Current.CancellationToken);
        var wrapper = Assert.IsType<RabbitConnectionWrapper>(rawWrapper);
        Assert.False(wrapper.InternalConnectionFactory.AutomaticRecoveryEnabled);
    }

    [Fact]
    public async Task Test_GetWrapperAsync()
    {
        var valueName = Guid.NewGuid().ToString();
        var valuePassword = Guid.NewGuid().ToString();
        var valueHostname = Guid.NewGuid().ToString();
        var valueVirtualHost = Guid.NewGuid().ToString();

        var configSource = new Mock<IRabbitMqServerConfigurationSource>(MockBehavior.Strict);
        configSource.Setup(cs => cs.GetConfigurationAsync(false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new RabbitMqServerConfigurationModel
            {
                Hostname = valueHostname,
                VirtualHost = valueVirtualHost,
                User = valueName,
                Password = valuePassword
            });

        var subscribeConfig = new Mock<IRabbitMqSubscribeConfigurationService>(MockBehavior.Strict);
        subscribeConfig.SetupGet(c => c.IsSubscription).Returns(false);

        var innerFactory = new InnerRabbitMqConnectionFactory(configSource.Object, subscribeConfig.Object);

        var rawWrapper =
            await innerFactory.GetConnectionFactoryWrapperAsync(false, TestContext.Current.CancellationToken);
        Assert.NotNull(rawWrapper);
        var wrapper = rawWrapper as RabbitConnectionWrapper;
        Assert.NotNull(wrapper);
        Assert.Equal(valueName, wrapper.InternalConnectionFactory.UserName);
        Assert.Equal(valuePassword, wrapper.InternalConnectionFactory.Password);
        Assert.Equal(valueHostname, wrapper.InternalConnectionFactory.HostName);
        Assert.Equal(valueVirtualHost, wrapper.InternalConnectionFactory.VirtualHost);
        Assert.Same(wrapper.InternalConnectionFactory, wrapper.ConnectionFactory);
        Assert.True(wrapper.InternalConnectionFactory.AutomaticRecoveryEnabled);
    }
}