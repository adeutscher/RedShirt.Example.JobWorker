using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal class KafkaJobModel : IJobModel
{
    internal required IKafkaMessageContainer Message { get; init; }
    public string MessageId => Message.MessageId;
    public string? IdempotencyId => Message.MessageIdIsDefault ? null : MessageId;
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}