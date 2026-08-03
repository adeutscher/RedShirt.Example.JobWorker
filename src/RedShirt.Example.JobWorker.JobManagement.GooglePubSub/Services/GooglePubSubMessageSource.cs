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
    IGooglePubSubRetryWrapperService retryWrapperService) : IGooglePubSubMessageSource
{
    /// <summary>
    ///     Unary Pull responses are capped at 1,000 messages by Pub/Sub.
    /// </summary>
    private const int MaxMessagesPerRequest = 1000;

    /// <summary>
    ///     Issues one unary Pull for up to <paramref name="pullSize" /> messages.
    /// </summary>
    private Task<List<IPubSubMessageContainer>> PullAsync(int pullSize,
        CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var client = await clientSource.GetSubscriberClientAsync(ct);
            var rawMessages = await client.GetMessagesAsync(pullSize, ct);
            return rawMessages.ToList();
        }, cancellationToken);
    }

    public async Task<List<IPubSubMessageContainer>> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<IPubSubMessageContainer>();

        while (batchSize > MaxMessagesPerRequest)
        {
            var loopResult = await PullAsync(MaxMessagesPerRequest, cancellationToken);

            messages.AddRange(loopResult);

            if (loopResult.Count < MaxMessagesPerRequest)
            {
                break;
            }

            batchSize -= MaxMessagesPerRequest;
        }

        if (batchSize is > 0 and <= MaxMessagesPerRequest)
        {
            messages.AddRange(await PullAsync(batchSize, cancellationToken));
        }

        return messages;
    }
}
