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
        configSource.Setup(cs => cs.GetConfigurationAsync(TestContext.Current.CancellationToken))
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

    private static void AssertFailoverUri(Uri brokerUri, string nestedBrokerUri)
    {
        var text = brokerUri.ToString();
        Assert.StartsWith("failover:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nestedBrokerUri, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("initialreconnectdelay=1000", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxreconnectdelay=30000", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("useexponentialbackoff=true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("tcp://localhost:61616",
        "failover:(tcp://localhost:61616)?transport.initialReconnectDelay=1000&transport.maxReconnectDelay=30000&transport.useExponentialBackOff=true")]
    [InlineData("tcp://localhost:1234/",
        "failover:(tcp://localhost:1234/)?transport.initialReconnectDelay=1000&transport.maxReconnectDelay=30000&transport.useExponentialBackOff=true")]
    [InlineData("failover:(tcp://broker:61616)", "failover:(tcp://broker:61616)")]
    [InlineData(
        "failover:(tcp://a:61616,tcp://b:61616)?transport.maxReconnectAttempts=5",
        "failover:(tcp://a:61616,tcp://b:61616)?transport.maxReconnectAttempts=5")]
    public void EnsureFailoverUri_WrapsPlainUriAndLeavesFailoverUri(string input, string expected)
    {
        Assert.Equal(expected, InnerActiveMqConnectionFactory.EnsureFailoverUri(input));
    }

    [Fact]
    public async Task GetWrapperAsync_WhenSubscriptionAndBrokerUriAlreadyFailover_DoesNotRewrap()
    {
        const string failoverUri = "failover:(tcp://broker:61616)?transport.maxReconnectAttempts=5";
        var configSource = CreateConfigSource(failoverUri);
        var subscribeConfiguration = CreateSubscribeConfiguration(true);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.Setup(c => c.GetBacklogSize()).Returns(1);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(
            await innerFactory.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken));

        var text = wrapper.InternalConnectionFactory.BrokerUri.ToString();
        Assert.StartsWith("failover:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tcp://broker:61616", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxreconnectattempts=5", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("initialreconnectdelay=1000", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWrapperAsync_WhenNotSubscription_LeavesPlainBrokerUriAndDefaultPrefetch()
    {
        var configSource = CreateConfigSource();
        var subscribeConfiguration = CreateSubscribeConfiguration(false);
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);

        var innerFactory = CreateFactory(configSource.Object, subscribeConfiguration.Object, coreConfiguration.Object);

        var wrapper = Assert.IsType<ActiveMqConnectionWrapper>(
            await innerFactory.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken));

        Assert.Equal(DefaultQueuePrefetch, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
        Assert.Equal(PlainBrokerUri, wrapper.InternalConnectionFactory.BrokerUri.ToString());
        coreConfiguration.Verify(c => c.GetBacklogSize(), Times.Never);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task GetWrapperAsync_WhenSubscription_SetsCredentialsFailoverUriAndQueuePrefetchFromBacklogSize(
        int backlogSize)
    {
        var valueName = Guid.NewGuid().ToString();
        var valuePassword = Guid.NewGuid().ToString();
        // The factory constructor insists that the URI be properly formatted.
        // ToStringing the URI also tacks a '/' onto the end. Go figure.
        var valueHostname = PlainBrokerUri;

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
        AssertFailoverUri(wrapper.InternalConnectionFactory.BrokerUri, valueHostname);
        Assert.Same(wrapper.InternalConnectionFactory, wrapper.ConnectionFactory);
        Assert.Equal(backlogSize, wrapper.InternalConnectionFactory.PrefetchPolicy.QueuePrefetch);
        coreConfiguration.Verify(c => c.GetBacklogSize(), Times.Once);
    }
}
