using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

internal class GooglePubSubJobModel : IJobModel
{
    internal required IPubSubMessageContainer Message { get; init; }
    public string MessageId => Message.Message?.Message?.MessageId ?? "UNKNOWN";
    public string? IdempotencyId => MessageId;
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}
