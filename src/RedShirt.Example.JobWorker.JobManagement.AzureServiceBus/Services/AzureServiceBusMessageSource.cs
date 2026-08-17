using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal interface IAzureServiceBusMessageSource
{
    Task<List<IServiceBusMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class AzureServiceBusMessageSource(
    IBusReceiverClientSource clientSource,
    IOptions<AzureServiceBusConfigurationModel> options) : IAzureServiceBusMessageSource
{
    private async Task<List<IServiceBusMessageContainer>> GetAsync(int batchSize,
        bool useWaitTimeSeconds,
        CancellationToken cancellationToken)
    {
        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        var rawMessages = await client.GetMessagesAsync(batchSize,
            useWaitTimeSeconds ? options.Value.EffectiveWaitTimeSeconds : null, cancellationToken);
        return rawMessages.ToList();
    }

    public async Task<List<IServiceBusMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<IServiceBusMessageContainer>();
        var firstRequest = true;

        while (batchSize > options.Value.MaxMessagesPerRequest)
        {
            var loopResult =
                await GetAsync(Math.Min(batchSize, options.Value.MaxMessagesPerRequest), firstRequest,
                    cancellationToken);
            firstRequest = false;

            messages.AddRange(loopResult);

            if (loopResult.Count < options.Value.MaxMessagesPerRequest)
            {
                // Received less than our batch size
                break;
            }

            batchSize -= options.Value.MaxMessagesPerRequest;
        }

        if (batchSize > 0 && batchSize <= options.Value.MaxMessagesPerRequest)
        {
            // Finish off requested size
            messages.AddRange(await GetAsync(batchSize, firstRequest, cancellationToken));
        }

        return messages;
    }
}