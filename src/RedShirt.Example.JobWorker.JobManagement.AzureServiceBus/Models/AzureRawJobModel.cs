using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal class AzureRawJobModel : IRawJobModel
{
    internal required IServiceBusMessageContainer Message { get; init; }
    public string MessageId => Message.Message?.MessageId ?? "UNKNOWN";
    public string? IdempotencyId => MessageId;
    public string? Body => Message.Message?.Body.ToString();
    public required DateTime CreatedAtUtc { get; init; }
}