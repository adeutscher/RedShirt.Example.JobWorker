using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
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

    public async Task AcknowledgeAsync(IRawJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not NatsRawJobModel jobModel)
        {
            return;
        }

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
        var fetchNoWaitOpts = new NatsJSFetchOpts
        {
            MaxMsgs = batchSize,
            IdleHeartbeat = TimeSpan.FromSeconds(5)
        };

        var getJobsResponseItems = new List<IRawJobModel>();

        var result = fetchNoWaitGetter.FetchNoWaitAsync(consumer, fetchNoWaitOpts, cancellationToken);

        await foreach (var msg in result)
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
    }
}