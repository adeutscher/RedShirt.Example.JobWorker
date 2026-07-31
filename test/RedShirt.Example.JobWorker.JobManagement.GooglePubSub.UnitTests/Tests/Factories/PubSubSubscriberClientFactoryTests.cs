using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using Microsoft.Extensions.Options;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Factories;

public class PubSubSubscriberClientFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldRequireProjectId(string? projectId)
    {
        var factory = new PubSubSubscriberClientFactory(Options.Create(new GooglePubSubConfigurationModel
        {
            ProjectId = projectId!,
            SubscriptionId = "jobs-subscription",
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60
        }));

        await Assert.ThrowsAsync<GooglePubSubSourceException>(() =>
            factory.GetSubscriberClientAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldRequireSubscriptionId(string? subscriptionId)
    {
        var factory = new PubSubSubscriberClientFactory(Options.Create(new GooglePubSubConfigurationModel
        {
            ProjectId = "local-pubsub",
            SubscriptionId = subscriptionId!,
            MaxMessagesPerRequest = 100,
            VisibilityTimeoutSeconds = 60
        }));

        await Assert.ThrowsAsync<GooglePubSubSourceException>(() =>
            factory.GetSubscriberClientAsync(TestContext.Current.CancellationToken));
    }
}
