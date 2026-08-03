namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

internal interface IPulsarMessageSourceResponse
{
    IReadOnlyList<IPulsarMessageContainer> Messages { get; }
}

internal sealed class PulsarMessageSourceResponse : IPulsarMessageSourceResponse
{
    public required IReadOnlyList<IPulsarMessageContainer> Messages { get; init; }
}