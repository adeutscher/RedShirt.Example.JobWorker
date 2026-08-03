using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;

internal interface IPulsarConsumerFactory
{
    Task<IPulsarConsumerWrapper> CreateConsumerAsync(CancellationToken cancellationToken = default);
}

internal class PulsarConsumerFactory(
    IPulsarRetryWrapperService retryWrapperService,
    IOptions<PulsarConsumerFactory.ConfigurationModel> options) : IPulsarConsumerFactory
{
    public async Task<IPulsarConsumerWrapper> CreateConsumerAsync(CancellationToken cancellationToken = default)
    {
#pragma warning disable S125
        /*
         * IMPORTANT:
         *  This general template is focused on Pulsar as a message source,
         *  not the details of Pulsar fine-tuning.
         *
         * In particular, this template was developed using a local standalone container with no authentication.
         * The actual implementation of authentication for Pulsar is left as an exercise for the developer
         * implementing this template.
         *
         * Pulsar.Client is used (rather than DotPulsar) because it exposes DeadLetterPolicy / MaxRedeliveryCount.
         *
         * Authentication is configured on PulsarClientBuilder via .Authentication(...).
         * Common approaches with Pulsar.Client (illustrative only — wire credentials from configuration/secrets):
         *
         *   // JWT / token auth
         *   var client = await new PulsarClientBuilder()
         *       .ServiceUrl(options.Value.ServiceUrl)
         *       .Authentication(AuthenticationFactory.Token("eyJhbGciOi..."))
         *       // or a supplier that refreshes the token:
         *       // .Authentication(AuthenticationFactory.Token(() => LoadTokenFromSomewhere()))
         *       .BuildAsync();
         *
         *   // TLS client-certificate auth (certFilePath is a PEM path the client loads)
         *   var client = await new PulsarClientBuilder()
         *       .ServiceUrl(options.Value.ServiceUrl) // typically pulsar+ssl://...
         *       .EnableTls(true)
         *       .Authentication(AuthenticationFactory.Tls("/path/to/client-cert.pem"))
         *       .BuildAsync();
         *
         *   // OAuth2 client credentials
         *   var client = await new PulsarClientBuilder()
         *       .ServiceUrl(options.Value.ServiceUrl)
         *       .Authentication(AuthenticationFactoryOAuth2.ClientCredentials(
         *           issuerUrl: new Uri("https://auth.example.com/"),
         *           audience: "urn:sn:pulsar:my-tenant:my-cluster",
         *           privateKey: new Uri("file:///path/to/credentials.json"),
         *           scope: "openid"))
         *       .BuildAsync();
         *
         * Prefer EnableTls / TlsTrustCertificate / AllowTlsInsecureConnection as appropriate for your broker TLS setup.
         */

#pragma warning restore S125

        /*
         * UNIT TESTING:
         *  A successful path through this method cannot be covered by unit tests. PulsarClientBuilder is constructed
         *  inline and immediately used via BuildAsync() / SubscribeAsync() with no injectable seam, so those calls
         *  always contact a real Pulsar broker. Offline unit tests therefore cover only pre-broker behaviour
         *  (early CancellationToken checks, ConfigurationModel defaults, and ParseSubscriptionType).
         */

        cancellationToken.ThrowIfCancellationRequested();

        // Requires a live Pulsar endpoint — see UNIT TESTING note above.
        var client = await new PulsarClientBuilder()
            .ServiceUrl(options.Value.ServiceUrl)
            .BuildAsync();

        cancellationToken.ThrowIfCancellationRequested();

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

        var consumer = await consumerBuilder.SubscribeAsync();

        return new PulsarConsumerWrapper(retryWrapperService, client, consumer, options.Value.Topic);
    }

    internal static SubscriptionType ParseSubscriptionType(string? subscriptionType)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (string.IsNullOrWhiteSpace(subscriptionType))
        {
            return SubscriptionType.Shared;
        }

        return Enum.Parse<SubscriptionType>(subscriptionType, true);
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