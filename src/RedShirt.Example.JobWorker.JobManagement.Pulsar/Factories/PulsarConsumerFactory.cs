using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;

internal interface IPulsarConsumerFactory
{
    IPulsarConsumerWrapper CreateConsumer();
}

internal class PulsarConsumerFactory(
    IPulsarRetryWrapperService retryWrapperService,
    IOptions<PulsarConsumerFactory.ConfigurationModel> options) : IPulsarConsumerFactory
{
    private static SubscriptionType ParseSubscriptionType(string? subscriptionType)
    {
        if (string.IsNullOrWhiteSpace(subscriptionType))
        {
            return SubscriptionType.Shared;
        }

        return Enum.Parse<SubscriptionType>(subscriptionType, true);
    }

    public IPulsarConsumerWrapper CreateConsumer()
    {
        /*
         * IMPORTANT:
         *  This general template is focused on Pulsar as a message source,
         *  not the details of Pulsar fine-tuning.
         *
         * In particular, this template was developed using a local standalone container with no authentication.
         * Implementing authentication is, for the moment, an exercise for the developer adapting this template.
         *
         * Pulsar.Client is used (rather than DotPulsar) because it exposes DeadLetterPolicy / MaxRedeliveryCount.
         */
        var client = new PulsarClientBuilder()
            .ServiceUrl(options.Value.ServiceUrl)
            .BuildAsync()
            .GetAwaiter()
            .GetResult();

        var subscriptionType = ParseSubscriptionType(options.Value.SubscriptionType);

        var consumerBuilder = client.NewConsumer(Schema.STRING(Encoding.UTF8))
            .Topic(options.Value.Topic)
            .SubscriptionName(options.Value.SubscriptionName)
            .SubscriptionType(subscriptionType)
            .SubscriptionInitialPosition(SubscriptionInitialPosition.Earliest)
            .DeadLetterPolicy(new DeadLetterPolicy(options.Value.MaxRedeliverCount))
            // Ack timeout is required for the client to increment redelivery counts toward MaxRedeliverCount
            // when messages remain unacknowledged (e.g. worker crash mid-batch).
            .AckTimeout(TimeSpan.FromSeconds(options.Value.AckTimeoutSeconds));

        var consumer = consumerBuilder
            .SubscribeAsync()
            .GetAwaiter()
            .GetResult();

        return new PulsarConsumerWrapper(retryWrapperService, client, consumer, options.Value.Topic);
    }

    public sealed class ConfigurationModel
    {
        public required string ServiceUrl { get; init; }
        public required string SubscriptionName { get; init; }
        public required string Topic { get; init; }

        /// <summary>
        ///     Pulsar subscription type. Defaults to Shared (competing consumers) when unset.
        /// </summary>
        public string? SubscriptionType { get; init; }

        /// <summary>
        ///     Maximum times a message may be redelivered before the client dead-letter policy
        ///     moves it to the dead letter topic. Mapped to <see cref="DeadLetterPolicy.MaxRedeliveryCount" />.
        /// </summary>
        public int MaxRedeliverCount { get; init; } = 3;

        /// <summary>
        ///     Seconds before an unacknowledged message is eligible for redelivery.
        ///     Mapped to the consumer <c>AckTimeout</c>. Defaults to 300 (5 minutes).
        /// </summary>
        public int AckTimeoutSeconds { get; init; } = 300;
    }
}