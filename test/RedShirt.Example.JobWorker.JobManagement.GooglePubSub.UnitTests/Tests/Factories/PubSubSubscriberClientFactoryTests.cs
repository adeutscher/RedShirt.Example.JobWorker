using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;

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
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = 3
        }));

        await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
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
            VisibilityTimeoutSeconds = 60,
            WaitTimeSeconds = 1,
            DlqNotEnabled = true,
            MaximumReceives = 3
        }));

        await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            factory.GetSubscriberClientAsync(TestContext.Current.CancellationToken));
    }
}