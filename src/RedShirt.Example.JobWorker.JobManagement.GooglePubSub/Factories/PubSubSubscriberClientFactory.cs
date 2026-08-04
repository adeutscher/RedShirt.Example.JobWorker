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
            throw new WorkerJobSourceException("No Google Pub/Sub project ID has been set")
            {
                CouldBeTransient = false,
                IsHandled = false,
                CouldBeExternallySolvable = false
            };
        }

        if (string.IsNullOrWhiteSpace(options.Value.SubscriptionId))
        {
            throw new WorkerJobSourceException("No Google Pub/Sub subscription ID has been set")
            {
                CouldBeTransient = false,
                IsHandled = false,
                CouldBeExternallySolvable = false
            };
        }

        /*
         * EmulatorDetection.EmulatorOrProduction is required for C# — unlike most other languages,
         * PUBSUB_EMULATOR_HOST alone is not enough for Google.Cloud.PubSub.V1.
         *
         * Untestable in unit tests (without production refactor):
         * SubscriberServiceApiClientBuilder.Build() is constructed inline with no seam to inject a
         * mock SubscriberServiceApiClient / builder. The happy path therefore always creates a real
         * gRPC client (credentials / channel / emulator detection), which is an integration concern.
         * Unit coverage is limited to the ProjectId / SubscriptionId validation throws above.
         * To unit-test client + SubscriptionName wrapping, introduce an injectable builder or
         * ClientFactory abstraction and assert wrapper construction against a Moq client.
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