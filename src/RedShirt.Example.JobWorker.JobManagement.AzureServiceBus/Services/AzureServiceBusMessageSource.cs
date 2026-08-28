using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal interface IAzureServiceBusMessageSource
{
    Task<List<IServiceBusMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class AzureServiceBusMessageSource(
    IAzureServiceBusClientRetryWrapper clientRetryWrapper,
    IOptions<AzureServiceBusConfigurationModel> options) : IAzureServiceBusMessageSource
{
    private async Task<List<IServiceBusMessageContainer>> GetAsync(int batchSize,
        bool useWaitTimeSeconds,
        CancellationToken cancellationToken)
    {
        List<IServiceBusMessageContainer>? result = null;
        await clientRetryWrapper.GetClientAndDoActionWithRetryAsync(async (client, ct) =>
        {
            var rawMessages = await client.GetMessagesAsync(batchSize,
                useWaitTimeSeconds ? options.Value.EffectiveWaitTimeSeconds : null, ct);
            // ReSharper disable once UseCollectionExpression
            result = rawMessages.ToList();
        }, cancellationToken: cancellationToken);
        return result ?? [];
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
                break;
            }

            batchSize -= options.Value.MaxMessagesPerRequest;
        }

        if (batchSize > 0 && batchSize <= options.Value.MaxMessagesPerRequest)
        {
            messages.AddRange(await GetAsync(batchSize, firstRequest, cancellationToken));
        }

        return messages;
    }
}