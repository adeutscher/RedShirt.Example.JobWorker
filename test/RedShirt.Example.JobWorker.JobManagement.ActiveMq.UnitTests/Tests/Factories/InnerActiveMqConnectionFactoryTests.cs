using Apache.NMS.ActiveMQ;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Factories;

public class InnerActiveMqConnectionFactoryTests
{
    private static readonly int DefaultQueuePrefetch =
        new ConnectionFactory("tcp://localhost:1234/").PrefetchPolicy.QueuePrefetch;

    private static Mock<IActiveMqServerConfigurationSource> CreateConfigSource()
    {
        var configSource = new Mock<IActiveMqServerConfigurationSource>(MockBehavior.Strict);
        configSource.Setup(cs => cs.GetConfigurationAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(new ActiveMqServerConfigurationModel
            {
                BrokerUri = "tcp://localhost:1234/",
                User = "u",
                Password = "p"
            });
        return configSource;
    }

    private static Mock<IActiveMqSubscribeConfigurationService> CreateSubscribeConfiguration(bool isSubscription)
    {
        var subscribeConfiguration = new Mock<IActiveMqSubscribeConfigurationService>(MockBehavior.Strict);
        subscribeConfiguration.SetupGet(s => s.IsSubscription).Returns(isSubscription);
        return subscribeConfiguration;
    }

    private static InnerActiveMqConnectionFactory CreateFactory(
        IActiveMqServerConfigurationSource configurationSource,
        IActiveMqSubscribeConfigurationService subscribeConfiguration,
        ICoreConfigurationService coreConfiguration)
    {
        return new InnerActiveMqConnectionFactory(configurationSource, subscribeConfiguration, coreConfiguration);
    }

    [Fact]
    public async Task GetWrapperAsync_WhenNotSubscription_LeavesDefaultQueuePrefetch()
    {
        var configSource = CreateConfigSource();
        var subscribeConfiguration = CreateSubscribeConfiguration(false);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(
            await innerFactory.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken));

        Assert.Equal(DefaultQueuePrefetch, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
        coreConfiguration.Verify(c => c.GetBacklogSize(), Times.Never);
    }

    [Fact]
    public async Task GetWrapperAsync_WhenSubscriptionAndBacklogSizeIsZero_UsesPrefetchOfOne()
    {
        var configSource = CreateConfigSource();
        var subscribeConfiguration = CreateSubscribeConfiguration(true);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.Setup(c => c.GetBacklogSize()).Returns(0);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(
            await innerFactory.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task GetWrapperAsync_WhenSubscription_SetsCredentialsUriAndQueuePrefetchFromBacklogSize(
        int backlogSize)
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

        var subscribeConfiguration = CreateSubscribeConfiguration(true);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.Setup(c => c.GetBacklogSize()).Returns(backlogSize);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var rawWrapper = await innerFactory.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(rawWrapper);
        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(rawWrapper);
        Assert.Equal(valueName, wrapper.InternalConnectionFactory.UserName);
        Assert.Equal(valuePassword, wrapper.InternalConnectionFactory.Password);
        Assert.Equal(valueHostname, wrapper.InternalConnectionFactory.BrokerUri.ToString());
        Assert.Same(wrapper.InternalConnectionFactory, wrapper.ConnectionFactory);
        Assert.Equal(backlogSize, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
        coreConfiguration.Verify(c => c.GetBacklogSize(), Times.Once);
    }
}