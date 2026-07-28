namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal interface IKafkaMessageSourceResponse
{
    IKafkaMessageContainer? LastMessage { get; init; }
    IReadOnlyList<IKafkaMessageContainer> Messages { get; }
}

internal sealed class KafkaMessageSourceResponse : IKafkaMessageSourceResponse
{
    public required IKafkaMessageContainer? LastMessage { get; init; }
    public required IReadOnlyList<IKafkaMessageContainer> Messages { get; init; }
}