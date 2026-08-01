using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal class AzureJobModel : IRawJobModel
{
    internal required IServiceBusMessageContainer Message { get; init; }
    public string MessageId => Message.Message?.MessageId ?? "UNKNOWN";
    public string? IdempotencyId => MessageId;
    public required string? Body { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}