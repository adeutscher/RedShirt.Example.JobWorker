using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

internal interface IPulsarMessageSource
{
    Task<IPulsarMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class PulsarMessageSource(
    IPulsarConsumerSource consumerSource,
    IPulsarRetryWrapperService retryWrapperService) : IPulsarMessageSource
{
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromSeconds(1);

    public async Task<IPulsarMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = consumerSource.GetConsumer();
        var messages = new List<IPulsarMessageContainer>();

        while (messages.Count < batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = await retryWrapperService.RunAsync(
                ct => consumer.ConsumeAsync(ConsumeTimeout, ct),
                cancellationToken);

            if (message is null)
            {
                break;
            }

            messages.Add(message);
        }

        return new PulsarMessageSourceResponse
        {
            Messages = messages
        };
    }
}
