using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal class AzureJobModel : IJobModel
{
    internal required IServiceBusMessageContainer Message { get; init; }
    public string MessageId => Message.Message?.MessageId ?? "UNKNOWN";
    public string? IdempotencyId => MessageId;
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}