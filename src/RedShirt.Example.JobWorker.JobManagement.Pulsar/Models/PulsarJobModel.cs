using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

internal class PulsarJobModel : IRawJobModel
{
    internal required IPulsarMessageContainer Message { get; init; }
    public string MessageId => Message.MessageId;
    public string? IdempotencyId => Message.MessageIdIsDefault ? null : MessageId;
    public required string? Body { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
