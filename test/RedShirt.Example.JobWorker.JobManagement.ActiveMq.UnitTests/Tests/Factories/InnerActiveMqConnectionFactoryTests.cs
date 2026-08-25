using Apache.NMS.ActiveMQ;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Factories;

public class InnerActiveMqConnectionFactoryTests
{
    private const string PlainBrokerUri = "tcp://localhost:1234/";

    private static readonly int DefaultQueuePrefetch =
        new ConnectionFactory(PlainBrokerUri).PrefetchPolicy.QueuePrefetch;

    private static Mock<IActiveMqServerConfigurationSource> CreateConfigSource(string brokerUri = PlainBrokerUri)
    {
        var configSource = new Mock<IActiveMqServerConfigurationSource>(MockBehavior.Strict);
        configSource
            .Setup(cs => cs.GetConfigurationAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveMqServerConfigurationModel
            {
                BrokerUri = brokerUri,
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetWrapperAsync_PassesForceNewSecretManagerPullToConfigurationSource(
        bool forceNewSecretManagerPull)
    {
        var configSource = CreateConfigSource();
        var subscribeConfiguration = CreateSubscribeConfiguration(false);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        await innerFactory.GetConnectionFactoryWrapperAsync(
            forceNewSecretManagerPull,
            TestContext.Current.CancellationToken);

        configSource.Verify(
            cs => cs.GetConfigurationAsync(forceNewSecretManagerPull, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetWrapperAsync_WhenNotSubscription_LeavesPlainBrokerUriAndDefaultPrefetch()
    {
        var configSource = CreateConfigSource();
        var subscribeConfiguration = CreateSubscribeConfiguration(false);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(
            await innerFactory.GetConnectionFactoryWrapperAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(DefaultQueuePrefetch, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
        Assert.Equal(PlainBrokerUri, wrapper.InternalConnectionFactory.BrokerUri.ToString());
        coreConfiguration.Verify(c => c.GetFetchCount(), Times.Never);
        configSource.Verify(cs => cs.GetConfigurationAsync(false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetWrapperAsync_WhenSubscriptionAndBrokerUriIsFailover_StripsFailover()
    {
        const string failoverUri = "failover:(tcp://broker:61616)?transport.maxReconnectAttempts=5";
        var configSource = CreateConfigSource(failoverUri);
        var subscribeConfiguration = CreateSubscribeConfiguration(true);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.Setup(c => c.GetFetchCount()).Returns(1);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(
            await innerFactory.GetConnectionFactoryWrapperAsync(
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("tcp://broker:61616/", wrapper.InternalConnectionFactory.BrokerUri.ToString());
        Assert.DoesNotContain("failover", wrapper.InternalConnectionFactory.BrokerUri.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task GetWrapperAsync_WhenSubscription_SetsCredentialsPlainUriAndQueuePrefetchFromBacklogSize(
        int backlogSize)
    {
        var valueName = Guid.NewGuid().ToString();
        var valuePassword = Guid.NewGuid().ToString();
        // The factory constructor insists that the URI be properly formatted.
        // ToStringing the URI also tacks a '/' onto the end. Go figure.
        var valueHostname = PlainBrokerUri;

        var configSource = new Mock<IActiveMqServerConfigurationSource>(MockBehavior.Strict);
        configSource
            .Setup(cs => cs.GetConfigurationAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveMqServerConfigurationModel
            {
                BrokerUri = valueHostname,
                User = valueName,
                Password = valuePassword
            });

        var subscribeConfiguration = CreateSubscribeConfiguration(true);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.Setup(c => c.GetFetchCount()).Returns(backlogSize);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var rawWrapper = await innerFactory.GetConnectionFactoryWrapperAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(rawWrapper);
        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(rawWrapper);
        Assert.Equal(valueName, wrapper.InternalConnectionFactory.UserName);
        Assert.Equal(valuePassword, wrapper.InternalConnectionFactory.Password);
        Assert.Equal(valueHostname, wrapper.InternalConnectionFactory.BrokerUri.ToString());
        Assert.Same(wrapper.InternalConnectionFactory, wrapper.ConnectionFactory);
        Assert.Equal(backlogSize, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
        coreConfiguration.Verify(c => c.GetFetchCount(), Times.Once);
    }

    [Theory]
    [InlineData("tcp://localhost:61616", "tcp://localhost:61616")]
    [InlineData("tcp://localhost:1234/", "tcp://localhost:1234/")]
    [InlineData("failover:(tcp://broker:61616)", "tcp://broker:61616")]
    [InlineData(
        "failover:(tcp://broker:61616)?transport.maxReconnectAttempts=5",
        "tcp://broker:61616")]
    [InlineData(
        "failover:(tcp://a:61616,tcp://b:61616)?transport.maxReconnectAttempts=5",
        "tcp://a:61616")]
    public void StripFailoverUri_RemovesFailoverWrapperAndLeavesPlainUri(string input, string expected)
    {
        Assert.Equal(expected, InnerActiveMqConnectionFactory.StripFailoverUri(input));
    }
}