using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;

internal interface IKafkaConsumerFactory
{
    IKafkaConsumerWrapper CreateConsumer();
}

internal class KafkaConsumerFactory(IOptions<KafkaConsumerFactory.ConfigurationModel> options) : IKafkaConsumerFactory
{
    public IKafkaConsumerWrapper CreateConsumer()
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Value.Topic);

        return new KafkaConsumerWrapper(consumer);
    }

    public sealed class ConfigurationModel
    {
        public required string BootstrapServers { get; init; }
        public required string GroupId { get; init; }
        public required string Topic { get; init; }
    }
}