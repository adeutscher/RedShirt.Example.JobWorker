using Apache.NMS;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

internal class ActiveMqRawJobModel : IRawJobModel
{
    /// <summary>
    ///     Get message body.
    /// </summary>
    /// <returns>The message body</returns>
    /// <exception cref="CouldNotRetrieveMessageBodyException"></exception>
    private string? GetBody()
    {
        switch (Message)
        {
            case ITextMessage textMsg:
                return textMsg.Text;
            case IBytesMessage bytesMsg:
            {
                // After receive, the read cursor may already be at EOF. Reset before reading
                // so we don't decode an untouched zero-filled buffer as NUL characters.
                bytesMsg.Reset();
                var content = new byte[bytesMsg.BodyLength];
                var bytesRead = bytesMsg.ReadBytes(content);
                return Encoding.UTF8.GetString(content, 0, bytesRead);
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