using Confluent.Kafka;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal interface IKafkaMessageContainer
{
    string? Key { get; }
    string? Value { get; }
    string Topic { get; }
    int Partition { get; }
    long Offset { get; }
    string MessageId { get; }
    bool MessageIdIsDefault { get; }
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
        ? DefaultMessageId
        : $"{Result.Topic}:{Result.Partition.Value}:{Result.Offset.Value}";

    public bool MessageIdIsDefault => Result is null;
    
    private const string DefaultMessageId = "UNKNOWN";
}