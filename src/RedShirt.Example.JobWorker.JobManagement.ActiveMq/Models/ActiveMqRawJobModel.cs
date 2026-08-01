using Apache.NMS;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

internal class ActiveMqRawJobModel : IRawJobModel
{
    private string? GetBody()
    {
        switch (Message)
        {
            case ITextMessage textMsg:
                return textMsg.Text;
            case IBytesMessage bytesMsg:
            {
                // Read bytes and convert to string manually
                var content = new byte[bytesMsg.BodyLength];
                bytesMsg.ReadBytes(content);
                return Encoding.UTF8.GetString(content);
            }
            default:
                // Ran out of options.
                throw new CouldNotRetrieveMessageBodyException();
        }
    }

    internal required IMessage Message { get; init; }
    public required string MessageId { get; init; }
    public string IdempotencyId => MessageId;
    public string? Body => GetBody();
    public required DateTime CreatedAtUtc { get; init; }
}