using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;

internal interface IKafkaConsumerFactory
{
    IKafkaConsumerWrapper CreateConsumer();
}

internal class KafkaConsumerFactory(
    IKafkaRetryWrapperService retryWrapperService,
    IOptions<KafkaConsumerFactory.ConfigurationModel> options) : IKafkaConsumerFactory
{
    public IKafkaConsumerWrapper CreateConsumer()
    {
#pragma warning disable S125
        /*
         * IMPORTANT:
         *  This general template is focused on Kafka as a message source,
         *  not the details of Kafka fine-tuning.
         *
         * In particular, this template was developed using a test container with no authentication
         *  and not one of the 5 available SASL options.
         * Implementing authentication is, for the moment, an exercise for the developer adapting this template.
         *
         * As written, SecurityProtocol is left at the librdkafka default of Plaintext: no TLS and no authentication.
         * Encryption and authentication are both configured on the ConsumerConfig below.
         * Common approaches with Confluent.Kafka (illustrative only, wire credentials from configuration/secrets):
         *
         *   // TLS only (encrypted, no client authentication)
         *   SecurityProtocol = SecurityProtocol.Ssl,
         *   // Only needed when the broker certificate is not signed by a CA in the system trust store:
         *   SslCaLocation = "/path/to/ca.pem",
         *
         *   // SASL/SCRAM over TLS (username and password, for example from the secret manager)
         *   SecurityProtocol = SecurityProtocol.SaslSsl,
         *   SaslMechanism = SaslMechanism.ScramSha512, // or ScramSha256, or Plain (only ever over TLS)
         *   SaslUsername = username,
         *   SaslPassword = password,
         *
         *   // Mutual TLS (client certificate)
         *   SecurityProtocol = SecurityProtocol.Ssl,
         *   SslCaLocation = "/path/to/ca.pem",
         *   SslCertificateLocation = "/path/to/client-cert.pem",
         *   SslKeyLocation = "/path/to/client-key.pem",
         *   SslKeyPassword = keyPassword, // if the key is encrypted
         *
         *   // SASL/OAUTHBEARER with OIDC client credentials
         *   SecurityProtocol = SecurityProtocol.SaslSsl,
         *   SaslMechanism = SaslMechanism.OAuthBearer,
         *   SaslOauthbearerMethod = SaslOauthbearerMethod.Oidc,
         *   SaslOauthbearerTokenEndpointUrl = "https://auth.example.com/oauth2/token",
         *   SaslOauthbearerClientId = clientId,
         *   SaslOauthbearerClientSecret = clientSecret,
         *   SaslOauthbearerScope = "kafka",
         *
         * Other cases:
         *  * Providers with their own token format (for example AWS MSK IAM) use SaslMechanism.OAuthBearer with a
         *    token supplied through ConsumerBuilder.SetOAuthBearerTokenRefreshHandler(...), usually from the provider's
         *    signer library.
         *  * SaslMechanism.Gssapi (Kerberos) also needs a keytab and Kerberos configuration in the container image.
         *
         * Keep SslEndpointIdentificationAlgorithm at its default (Https) so that the broker's hostname is checked.
         * A failed SASL handshake surfaces as ErrorCode.SaslAuthenticationFailed, which KafkaExceptionArbiterService
         * already classifies.
         */
#pragma warning restore S125

        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.GroupId,
            // KafkaJobSource is responsible for driving commits
            EnableAutoCommit = false,
            /*
             * Intentional: a consumer group with no committed offsets starts from the earliest retained message.
             *
             * A new or changed GroupId therefore reprocesses everything still retained on the topic.
             * This is accepted because a changed GroupId suggests a different applied version of the JobWorker, which
             * should not skip messages that its predecessor's group already handled under different logic.
             *
             * An applied version should consider replacing the configurable GroupId with a constant, so that a
             * configuration typo or an environment variable left over from another deployment cannot start a full replay.
             */
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Value.Topic);

        return new KafkaConsumerWrapper(retryWrapperService, consumer);
    }

    public sealed class ConfigurationModel
    {
        public required string BootstrapServers { get; init; }
        public required string GroupId { get; init; }
        public required string Topic { get; init; }
    }
}