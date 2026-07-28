using Confluent.Kafka;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

internal interface IKafkaConsumerWrapper : IDisposable
{
    void Commit(IEnumerable<IKafkaMessageContainer> messages);
    IKafkaMessageContainer? Consume(TimeSpan timeout);
}

internal class KafkaConsumerWrapper(IConsumer<string, string> consumer) : IKafkaConsumerWrapper
{
    internal IConsumer<string, string> Client => consumer;

    public IKafkaMessageContainer? Consume(TimeSpan timeout)
    {
        var result = Client.Consume(timeout);
        if (result?.Message is null)
        {
            return null;
        }

        return new KafkaMessageContainer
        {
            Result = result
        };
    }

    public void Commit(IEnumerable<IKafkaMessageContainer> messages)
    {
        var offsets = messages
            .Select(m => new TopicPartitionOffset(m.Topic, m.Partition, new Offset(m.Offset + 1)))
            .ToList();

        if (offsets.Count == 0)
        {
            return;
        }

        Client.Commit(offsets);
    }

    public void Dispose()
    {
        Client.Close();
        Client.Dispose();
    }
}