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
        /*
         * IMPORTANT:
         *  This general template is focused on Kafka as a message source,
         *  not the details of Kafka fine-tuning.
         *
         * In particular, this template was developed using a test container with no authentication
         *  and not one of the 5 available SASL options.
         * Implementing authentication is, for the moment, an excercise for the developer adapting this template.
         */
        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.GroupId,
            // KafkaJobSource is responsible for driving commits
            EnableAutoCommit = false,
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