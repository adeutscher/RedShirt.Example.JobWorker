using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Models;

internal class JobModel : IJobModel
{
    internal required INatsJSMsg<NatsMemoryOwner<byte>> Message { get; init; }
    public required string MessageId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}