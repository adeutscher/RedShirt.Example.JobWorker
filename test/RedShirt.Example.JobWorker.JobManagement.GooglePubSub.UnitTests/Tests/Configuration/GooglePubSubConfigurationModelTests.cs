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
            VisibilityTimeoutSeconds = configured,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = 3
        };

        Assert.Equal(expected, options.EffectiveVisibilityTimeoutSeconds);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(120, 60)]
    public void ShouldClampWaitTimeSeconds(int configured, int expected)
    {
        var options = new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = "jobs-subscription",
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = configured,
            DlqNotEnabled = true,
            MaximumReceives = 3
        };

        Assert.Equal(expected, options.EffectiveWaitTimeSeconds);
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
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = configured
        };

        Assert.Equal(expected, options.EffectiveMaximumReceives);
    }
}