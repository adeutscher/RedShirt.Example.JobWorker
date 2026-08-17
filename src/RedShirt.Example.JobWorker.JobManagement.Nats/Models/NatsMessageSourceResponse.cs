using NATS.Client.Core;
using NATS.Client.JetStream;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Models;

internal class NatsMessageSourceResponse
{
    public required List<INatsJSMsg<NatsMemoryOwner<byte>>> Messages { get; init; }
}