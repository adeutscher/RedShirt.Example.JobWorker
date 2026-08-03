using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

internal class GooglePubSubJobModel : IRawJobModel
{
    internal required IPubSubMessageContainer Message { get; init; }
    public string MessageId =>
        string.IsNullOrEmpty(Message.Message?.Message?.MessageId) ? "UNKNOWN" : Message.Message.Message.MessageId;
    public string? IdempotencyId => MessageId;
    public string? Body => Message.Message?.Message?.Data.ToStringUtf8();
    public required DateTime CreatedAtUtc { get; init; }
}
