using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Core.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Models;

internal class NatsRawJobModel : IRawJobModel
{
    private string GetBody()
    {
        return Encoding.UTF8.GetString(Message.Data.Span);
    }

    internal required INatsJSMsg<NatsMemoryOwner<byte>> Message { get; init; }
    public required string MessageId { get; init; }
    public string? IdempotencyId => MessageId;
    public string? Body => GetBody();
    public required DateTime CreatedAtUtc { get; init; }
}