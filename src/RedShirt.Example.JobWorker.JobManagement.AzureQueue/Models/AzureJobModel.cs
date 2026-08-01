using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

internal class AzureJobModel : IRawJobModel
{
    internal required IQueueMessageModel Message { get; init; }
    public string MessageId => Message.MessageId;
    public string? IdempotencyId => MessageId;
    public required string? Body { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}