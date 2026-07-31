using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Configuration;

public class GooglePubSubConfigurationModelTests
{
    [Theory]
    [InlineData(5, 10)]
    [InlineData(10, 10)]
    [InlineData(60, 60)]
    [InlineData(600, 600)]
    [InlineData(1200, 600)]
    public void ShouldClampAckDeadline(int configured, int expected)
    {
        var options = new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = configured,
            DlqNotEnabled = true,
            MaximumReceives = 3
        };

        Assert.Equal(expected, options.EffectiveVisibilityTimeoutSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    public void ShouldFloorMaximumReceives(int configured, int expected)
    {
        var options = new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60,
            DlqNotEnabled = true,
            MaximumReceives = configured
        };

        Assert.Equal(expected, options.EffectiveMaximumReceives);
    }
}