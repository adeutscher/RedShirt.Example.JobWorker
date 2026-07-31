namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal interface IKafkaMessageSourceResponse
{
    IReadOnlyList<IKafkaMessageContainer> Messages { get; }
}

internal sealed class KafkaMessageSourceResponse : IKafkaMessageSourceResponse
{
    public required IReadOnlyList<IKafkaMessageContainer> Messages { get; init; }
}