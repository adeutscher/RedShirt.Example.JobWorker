using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal interface INatsMessageSource
{
    Task<NatsMessageSourceResponse> FetchMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}

internal class NatsMessageSource(
    INatsConsumerSource consumerSource,
    IOptions<NatsMessageSource.ConfigurationModel> options) : INatsMessageSource
{
    private static readonly TimeSpan HeartbeatTime = TimeSpan.FromSeconds(5);

    private static async Task<List<INatsJSMsg<NatsMemoryOwner<byte>>>> FetchBatchWithNoWaitAsync(int batchSize,
        INatsJSConsumer consumer, CancellationToken cancellationToken)
    {
        var items = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();

        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = batchSize,
            IdleHeartbeat = HeartbeatTime
        };

        var result = consumer.FetchNoWaitAsync<NatsMemoryOwner<byte>>(fetchOpts, cancellationToken: cancellationToken);
        await foreach (var msg in result)
        {
            items.Add(msg);
        }

        return items;
    }

    public async Task<NatsMessageSourceResponse> FetchMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = await consumerSource.GetConsumerAsync(cancellationToken);

        if (options.Value.EffectiveWaitTimeSeconds <= 0)
        {
            return new NatsMessageSourceResponse
            {
                Messages = await FetchBatchWithNoWaitAsync(batchSize, consumer, cancellationToken)
            };
        }

        var items = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();
        var firstResult = await consumer.NextAsync<NatsMemoryOwner<byte>>(opts: new NatsJSNextOpts
        {
            IdleHeartbeat = HeartbeatTime,
            Expires = TimeSpan.FromSeconds(options.Value.EffectiveWaitTimeSeconds)
        }, cancellationToken: cancellationToken);

        if (firstResult is null)
        {
            return new NatsMessageSourceResponse
            {
                Messages = items
            };
        }

        // Result is not null

        items.Add(firstResult);
        if (batchSize >= 1)
        {
            // Remaining items to follow up on after getting next
            items.AddRange(await FetchBatchWithNoWaitAsync(batchSize - 1, consumer, cancellationToken));
        }

        return new NatsMessageSourceResponse
        {
            Messages = items
        };
    }

    public sealed class ConfigurationModel
    {
        public required int WaitTimeSeconds { get; init; }
        public int EffectiveWaitTimeSeconds => Math.Max(WaitTimeSeconds, 0);
    }
}