using Apache.NMS;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

public interface IActiveMqMessageBodyRetriever
{
    string? GetMessageBody(IMessage message);
}

public class ActiveMqMessageBodyRetriever : IActiveMqMessageBodyRetriever
{
    public string? GetMessageBody(IMessage message)
    {
        if (message is ITextMessage textMsg)
        {
            return textMsg.Text;
        }

        if (message is IBytesMessage bytesMsg)
        {
            // Read bytes and convert to string manually
            var content = new byte[bytesMsg.BodyLength];
            bytesMsg.ReadBytes(content);
            return Encoding.UTF8.GetString(content);
        }

        throw new CouldNotRetrieveMessageBodyException();
    }
}