using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal class KafkaJobModel : IJobModel
{
    internal required IKafkaMessageContainer Message { get; init; }
    public string MessageId => Message.MessageId;
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}