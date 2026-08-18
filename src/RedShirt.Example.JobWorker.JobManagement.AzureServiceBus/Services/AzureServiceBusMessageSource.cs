using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal interface IAzureServiceBusMessageSource
{
    Task<List<IServiceBusMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class AzureServiceBusMessageSource(
    IBusReceiverClientSource clientSource,
    IAzureServiceBusRetryWrapperService retryWrapperService,
    IOptions<AzureServiceBusConfigurationModel> options) : IAzureServiceBusMessageSource
{
    private Task<List<IServiceBusMessageContainer>> GetAsync(int batchSize,
        bool useWaitTimeSeconds,
        CancellationToken cancellationToken)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetQueueClientAsync(ct);
            var rawMessages = await client.GetMessagesAsync(batchSize,
                useWaitTimeSeconds ? options.Value.EffectiveWaitTimeSeconds : null, ct);
            return rawMessages.ToList();
        }, cancellationToken);
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