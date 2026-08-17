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
    /// <summary>
    ///     Unary Pull responses are capped at 1,000 messages by Pub/Sub.
    /// </summary>
    private const int MaxMessagesPerRequest = 1000;

    /// <summary>
    ///     Issues one unary Pull for up to <paramref name="pullSize" /> messages.
    /// </summary>
    private Task<List<IPubSubMessageContainer>> PullAsync(int pullSize, bool useConfiguredWaitTime,
        CancellationToken cancellationToken)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetSubscriberClientAsync(ct);
            var rawMessages = await client.GetMessagesAsync(pullSize,
                useConfiguredWaitTime ? options.Value.EffectiveWaitTimeSeconds : 0, ct);
            return rawMessages.ToList();
        }, cancellationToken);
    }

    public async Task<List<IPubSubMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<IPubSubMessageContainer>();
        var firstRequest = true;

        while (batchSize > MaxMessagesPerRequest)
        {
            // Mostly declaring pullSize separately to make a point for readability.
            var pullSize = MaxMessagesPerRequest;
            var loopResult = await PullAsync(pullSize, firstRequest, cancellationToken);
            firstRequest = false;

            messages.AddRange(loopResult);

            if (loopResult.Count < pullSize)
            {
                break;
            }

            batchSize -= pullSize;
        }

        if (batchSize is > 0 and <= MaxMessagesPerRequest)
        {
            messages.AddRange(await PullAsync(batchSize, firstRequest, cancellationToken));
        }

        return messages;
    }
}