using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;

internal interface IKafkaConsumerSource
{
    IKafkaConsumerWrapper GetConsumer();
}

internal class KafkaConsumerSource(IKafkaConsumerFactory factory) : IKafkaConsumerSource
{
    private readonly Lazy<IKafkaConsumerWrapper> _consumer = new(factory.CreateConsumer);

    public IKafkaConsumerWrapper GetConsumer()
    {
        return _consumer.Value;
    }
}