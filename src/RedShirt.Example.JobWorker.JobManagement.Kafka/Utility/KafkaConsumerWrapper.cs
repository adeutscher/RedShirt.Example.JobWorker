using Confluent.Kafka;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

internal interface IKafkaConsumerWrapper : IDisposable
{
    void Commit(IKafkaMessageContainer? targetLastMessage);
    IKafkaMessageContainer? Consume(TimeSpan timeout);
}

internal class KafkaConsumerWrapper(IConsumer<string, string> consumer) : IKafkaConsumerWrapper
{
    internal IConsumer<string, string> Client => consumer;

    public IKafkaMessageContainer? Consume(TimeSpan timeout)
    {
        var result = Client.Consume(timeout);
        if (result?.Message is null)
        {
            return null;
        }

        return new KafkaMessageContainer
        {
            Result = result
        };
    }

    public void Commit(IKafkaMessageContainer? targetLastMessage)
    {
        if (targetLastMessage is null)
        {
            return;
        }

        Client.Commit(targetLastMessage.Result);
    }

    public void Dispose()
    {
        Client.Close();
        Client.Dispose();
    }
}