using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal class NatsJobSource(
    INatsConsumerSource consumerSource,
    INatsMessageSource messageSource,
    ILogger<NatsJobSource> logger,
    IOptions<NatsStreamConfigurationModel> options) : IJobSource
{
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

    public bool IsSubscriptionSource => false;

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from NATS Stream: {StreamName}",
            batchSize, options.Value.StreamName);

        var getJobsResponseItems = new List<IRawJobModel>();

        var messageResult = await messageSource.FetchMessagesAsync(batchSize, cancellationToken);

        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (var msg in messageResult.Messages)
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

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public void StopSubscriber()
    {
        throw new NotSupportedException();
    }
}