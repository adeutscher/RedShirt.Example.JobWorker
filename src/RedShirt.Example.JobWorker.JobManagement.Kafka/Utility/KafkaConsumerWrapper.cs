using Confluent.Kafka;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

internal interface IKafkaMessageContainer
{
    string? Key { get; }
    string? Value { get; }
    string Topic { get; }
    int Partition { get; }
    long Offset { get; }
    string MessageId { get; }
}

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

    internal class KafkaMessageContainer : IKafkaMessageContainer
    {
        public required ConsumeResult<string, string>? Result { get; init; }

        public string? Key => Result?.Message?.Key;
        public string? Value => Result?.Message?.Value;
        public string Topic => Result?.Topic ?? string.Empty;
        public int Partition => Result?.Partition.Value ?? -1;
        public long Offset => Result?.Offset.Value ?? -1;

        public string MessageId => Result is null
            ? "UNKNOWN"
            : $"{Result.Topic}:{Result.Partition.Value}:{Result.Offset.Value}";
    }
}