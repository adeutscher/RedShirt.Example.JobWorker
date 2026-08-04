using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal interface IKafkaMessageSource
{
    Task<IKafkaMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class KafkaMessageSource(
    IKafkaConsumerSource consumerSource,
    IKafkaRetryWrapperService retryWrapperService) : IKafkaMessageSource
{
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromSeconds(1);

    public async Task<IKafkaMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = consumerSource.GetConsumer();
        var messages = new List<IKafkaMessageContainer>();

        while (messages.Count < batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = await retryWrapperService.RunAsync(
                _ => Task.FromResult(consumer.Consume(ConsumeTimeout)),
                cancellationToken);

            if (message is null)
            {
                break;
            }

            messages.Add(message);
        }

        return new KafkaMessageSourceResponse
        {
            Messages = messages
        };
    }
}