using NATS.Client.Core;
using NATS.Client.JetStream;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Utility;

public interface IBodyRetriever
{
    string GetMessageBody(INatsJSMsg<NatsMemoryOwner<byte>> input);
}

/// <summary>
///     Retrieve a body out of a NatsJSMsg.
///     Written to separate some difficult-to-mock logic from NatsJobSource
/// </summary>
public class BodyRetriever : IBodyRetriever
{
    public string GetMessageBody(INatsJSMsg<NatsMemoryOwner<byte>> input)
    {
        return Encoding.UTF8.GetString(input.Data.Span);
    }
}