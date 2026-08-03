using Microsoft.Extensions.Options;
using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Factories;

public class PulsarConsumerFactoryTests
{
    /*
     * CreateConsumerAsync cannot be unit-tested for a successful subscribe path:
     * PulsarConsumerFactory constructs a real PulsarClientBuilder, calls BuildAsync(), then
     * SubscribeAsync() with no injectable seam. Those calls require a reachable Pulsar broker
     * (and typically a pre-created topic). Cover factory behavior that does not need a broker
     * instead: configuration defaults, subscription-type parsing, and early cancellation.
     */

    private static PulsarConsumerFactory CreateFactory(
        string? subscriptionType = null,
        int maxRedeliverCount = 5,
        int ackTimeoutSeconds = 120)
    {
        return new PulsarConsumerFactory(
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new PulsarConsumerFactory.ConfigurationModel
            {
                ServiceUrl = "pulsar://localhost:6650",
                SubscriptionName = "test-group",
                Topic = "persistent://public/default/test-topic",
                SubscriptionType = subscriptionType,
                MaxRedeliverCount = maxRedeliverCount,
                AckTimeoutSeconds = ackTimeoutSeconds
            }));
    }

    [Fact]
    public void ConfigurationModel_DefaultsMaxRedeliverCountAndAckTimeout()
    {
        var model = new PulsarConsumerFactory.ConfigurationModel
        {
            ServiceUrl = "pulsar://localhost:6650",
            SubscriptionName = "test-group",
            Topic = "persistent://public/default/test-topic"
        };

        Assert.Equal(3, model.MaxRedeliverCount);
        Assert.Equal(300, model.AckTimeoutSeconds);
    }

    [Fact]
    public async Task CreateConsumerAsync_WhenCancellationRequested_ThrowsBeforeBrokerContact()
    {
        var factory = CreateFactory();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            factory.CreateConsumerAsync(cts.Token));
    }

    [Fact]
    public void ParseSubscriptionType_InvalidValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PulsarConsumerFactory.ParseSubscriptionType("not-a-type"));
    }

    [Theory]
    [InlineData(null, SubscriptionType.Shared)]
    [InlineData("", SubscriptionType.Shared)]
    [InlineData("   ", SubscriptionType.Shared)]
    [InlineData("Shared", SubscriptionType.Shared)]
    [InlineData("shared", SubscriptionType.Shared)]
    [InlineData("Failover", SubscriptionType.Failover)]
    [InlineData("KeyShared", SubscriptionType.KeyShared)]
    [InlineData("Exclusive", SubscriptionType.Exclusive)]
    public void ParseSubscriptionType_MapsConfiguredValues(string? subscriptionType,
        SubscriptionType expected)
    {
        Assert.Equal(expected, PulsarConsumerFactory.ParseSubscriptionType(subscriptionType));
    }
}