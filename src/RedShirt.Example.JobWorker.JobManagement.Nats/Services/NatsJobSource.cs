using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal class NatsJobSource(
    INatsJetStreamContextFactory natsJetStreamContextFactory,
    IFetchNoWaitGetter fetchNoWaitGetter,
    ILogger<NatsJobSource> logger,
    IOptions<NatsJobSource.ConfigurationModel> options) : IJobSource
{
    private readonly Lazy<Task<INatsJSContext>> _lazyContext =
        new(() => natsJetStreamContextFactory.CreateNatsJetStreamContextAsync());

#pragma warning disable S2325
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
#pragma warning restore S2325
        CancellationToken cancellationToken = default)
    {
        if (message is not NatsRawJobModel jobModel)
        {
            return;
        }

        // Intentionally not using result
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue
        _ = result;

        // Ack. Whether result successful, a recoverable failures, or an unrecoverable failure.
        // JetStream dead-lettering is typically consumer/policy based, so this should be handled
        //  by the IJobFailureHandler implementation on an application-to-application basis.
        await jobModel.Message.AckAsync(cancellationToken: cancellationToken);
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from NATS Stream: {StreamName}",
            batchSize, options.Value.StreamName);

        var js = await _lazyContext.Value;

        var consumer = await js.CreateOrUpdateConsumerAsync(options.Value.StreamName,
            new ConsumerConfig {Name = "c1", DurableName = "c1"}, cancellationToken);
        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = batchSize,
            Expires = options.Value.EffectiveWaitTimeSeconds > 0
                ? TimeSpan.FromSeconds(options.Value.EffectiveWaitTimeSeconds)
                : null,
            IdleHeartbeat = TimeSpan.FromSeconds(5)
        };

        var getJobsResponseItems = new List<IRawJobModel>();

        IAsyncEnumerable<INatsJSMsg<NatsMemoryOwner<byte>>> result;

        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
        if (fetchOpts.Expires.HasValue)
        {
            result = consumer.FetchAsync<NatsMemoryOwner<byte>>(fetchOpts, cancellationToken: cancellationToken);
        }
        else
        {
            result = fetchNoWaitGetter.FetchNoWaitAsync(consumer, fetchOpts, cancellationToken);
        }

        await foreach (var msg in result.WithCancellation(cancellationToken))
        {
            // Got a message, add it to return set.
            getJobsResponseItems.Add(new NatsRawJobModel
            {
                Message = msg,
                MessageId = msg.Metadata?.Sequence.Stream.ToString() ?? "UNKNOWN",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return new JobSourceResponse
        {
            Items = getJobsResponseItems
        };
    }

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        public required string StreamName { get; init; }
        public required int WaitTimeSeconds { get; init; }
        public int EffectiveWaitTimeSeconds => Math.Max(WaitTimeSeconds, 0);
    }
}