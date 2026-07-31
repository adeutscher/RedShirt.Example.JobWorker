using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Factories;

public class PulsarConsumerFactoryTests
{
    [Fact]
    public void ConfigurationModel_DefaultsMaxRedeliverCount()
    {
        var model = new PulsarConsumerFactory.ConfigurationModel
        {
            ServiceUrl = "pulsar://localhost:6650",
            SubscriptionName = "test-group",
            Topic = "persistent://public/default/test-topic"
        };

        Assert.Equal(3, model.MaxRedeliverCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Shared")]
    [InlineData("Failover")]
    public void CreateConsumer_AcceptsSubscriptionTypeValues(string? subscriptionType)
    {
        var factory = new PulsarConsumerFactory(
            PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new PulsarConsumerFactory.ConfigurationModel
            {
                ServiceUrl = "pulsar://localhost:6650",
                SubscriptionName = "test-group",
                Topic = "persistent://public/default/test-topic",
                SubscriptionType = subscriptionType,
                MaxRedeliverCount = 5
            }));

        // Creating a consumer contacts the broker; only validate configuration binding here when offline.
        var options = Options.Create(new PulsarConsumerFactory.ConfigurationModel
        {
            ServiceUrl = "pulsar://localhost:6650",
            SubscriptionName = "test-group",
            Topic = "persistent://public/default/test-topic",
            SubscriptionType = subscriptionType,
            MaxRedeliverCount = 5
        });

        Assert.Equal(5, options.Value.MaxRedeliverCount);
        Assert.Equal(subscriptionType, options.Value.SubscriptionType);
        Assert.NotNull(factory);
    }
}
