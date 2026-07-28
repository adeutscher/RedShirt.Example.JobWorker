using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal interface IKafkaMessageSource
{
    Task<IKafkaMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class KafkaMessageSource(IKafkaConsumerSource consumerSource) : IKafkaMessageSource
{
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromSeconds(1);

    public Task<IKafkaMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = consumerSource.GetConsumer();
        var messages = new List<IKafkaMessageContainer>();

        IKafkaMessageContainer? lastMessage = null;

        while (messages.Count < batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = consumer.Consume(ConsumeTimeout);

            if (message is null)
            {
                break;
            }

            lastMessage = message;
            messages.Add(message);
        }

        return Task.FromResult<IKafkaMessageSourceResponse>(new KafkaMessageSourceResponse
        {
            Messages = messages,
            LastMessage = lastMessage
        });
    }
}