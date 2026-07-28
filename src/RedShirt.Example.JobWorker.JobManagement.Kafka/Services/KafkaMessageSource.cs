using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal interface IKafkaMessageSource
{
    Task<List<IKafkaMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class KafkaMessageSource(IKafkaConsumerSource consumerSource) : IKafkaMessageSource
{
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromSeconds(1);

    public Task<List<IKafkaMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = consumerSource.GetConsumer();
        var messages = new List<IKafkaMessageContainer>();

        while (messages.Count < batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = consumer.Consume(ConsumeTimeout);
            if (message is null)
            {
                break;
            }

            messages.Add(message);
        }

        return Task.FromResult(messages);
    }
}