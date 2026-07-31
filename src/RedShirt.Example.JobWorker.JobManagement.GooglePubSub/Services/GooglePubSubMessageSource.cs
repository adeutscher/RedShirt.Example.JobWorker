using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal interface IGooglePubSubMessageSource
{
    Task<List<IPubSubMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class GooglePubSubMessageSource(
    IPubSubSubscriberClientSource clientSource,
    IOptions<GooglePubSubConfigurationModel> options) : IGooglePubSubMessageSource
{
    private async Task<List<IPubSubMessageContainer>> GetAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var client = await clientSource.GetSubscriberClientAsync(cancellationToken);
        var rawMessages = await client.GetMessagesAsync(batchSize, cancellationToken);
        return rawMessages.ToList();
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
