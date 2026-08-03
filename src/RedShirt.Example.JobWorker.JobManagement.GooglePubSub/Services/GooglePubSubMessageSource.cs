using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal interface IGooglePubSubMessageSource
{
    Task<List<IPubSubMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class GooglePubSubMessageSource(
    IPubSubSubscriberClientSource clientSource,
    IGooglePubSubRetryWrapperService retryWrapperService,
    IOptions<GooglePubSubConfigurationModel> options) : IGooglePubSubMessageSource
{
    private Task<List<IPubSubMessageContainer>> GetAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetSubscriberClientAsync(ct);
            var rawMessages = await client.GetMessagesAsync(batchSize, ct);
            return rawMessages.ToList();
        }, cancellationToken);
    }

    public async Task<List<IPubSubMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<IPubSubMessageContainer>();

        while (batchSize > options.Value.MaxMessagesPerRequest)
        {
            var loopResult =
                await GetAsync(Math.Min(batchSize, options.Value.MaxMessagesPerRequest), cancellationToken);

            messages.AddRange(loopResult);

            if (loopResult.Count < options.Value.MaxMessagesPerRequest)
            {
                break;
            }

            batchSize -= options.Value.MaxMessagesPerRequest;
        }

        if (batchSize > 0 && batchSize <= options.Value.MaxMessagesPerRequest)
        {
            messages.AddRange(await GetAsync(batchSize, cancellationToken));
        }

        return messages;
    }
}