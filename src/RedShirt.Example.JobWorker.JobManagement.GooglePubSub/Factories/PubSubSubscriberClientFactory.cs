using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;

internal interface IPubSubSubscriberClientFactory
{
    Task<IPubSubSubscriberClientWrapper> GetSubscriberClientAsync(CancellationToken cancellationToken = default);
}

internal class PubSubSubscriberClientFactory(IOptions<GooglePubSubConfigurationModel> options)
    : IPubSubSubscriberClientFactory
{
    public Task<IPubSubSubscriberClientWrapper> GetSubscriberClientAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ProjectId))
        {
            throw new WorkerJobSourceException("No Google Pub/Sub project ID has been set", false);
        }

        if (string.IsNullOrWhiteSpace(options.Value.SubscriptionId))
        {
            throw new WorkerJobSourceException("No Google Pub/Sub subscription ID has been set", false);
        }

        /*
         * EmulatorDetection.EmulatorOrProduction is required for C# — unlike most other languages,
         * PUBSUB_EMULATOR_HOST alone is not enough for Google.Cloud.PubSub.V1.
         */
        var client = new SubscriberServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction
        }.Build();

        var subscriptionName =
            SubscriptionName.FromProjectSubscription(options.Value.ProjectId, options.Value.SubscriptionId);

        return Task.FromResult<IPubSubSubscriberClientWrapper>(
            new PubSubSubscriberClientWrapper(client, subscriptionName));
    }
}