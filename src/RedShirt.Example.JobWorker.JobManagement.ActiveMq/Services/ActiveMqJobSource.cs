using Apache.NMS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal class ActiveMqJobSource(
    IActiveMqRetryWrapperService retryWrapperService,
    IActiveMqConsumerRetryWrapper consumerRetryWrapper,
    IOptions<ActiveMqConfigurationModel> configuration,
    ILogger<ActiveMqJobSource> logger)
    : IJobSource
{
    public int RecommendedHeartbeatIntervalSeconds => 0;

    public bool IsSubscriptionSource => false;

#pragma warning disable S2325
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not ActiveMqRawJobModel jobModel)
        {
            return;
        }

        // Intentionally not using result
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue
        _ = result;

        // Acknowledge whether successful, recoverable, or unrecoverable
        // (ActiveMQ client API has no direct dead-letter call here).
        await retryWrapperService.RunAsync(
            _ => jobModel.Message.AcknowledgeAsync(),
            cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Max(1, batchSize);

        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from ActiveMQ Queue: {QueueName}",
            batchSize, configuration.Value.QueueName);

        var getJobsResponseItems = new List<IRawJobModel>();

        while (getJobsResponseItems.Count < batchSize)
        {
            IMessage? result = null;

            await consumerRetryWrapper.GetChannelAndDoActionWithRetryAsync(
                async (consumer, _) => { result = await consumer.ReceiveAsync(TimeSpan.FromMilliseconds(100)); },
                cancellationToken: cancellationToken);

            if (result is null)
                // Nothing more to grab at the moment.
            {
                break;
            }

            // Got a message, add it to return set.
            getJobsResponseItems.Add(new ActiveMqRawJobModel
            {
                Message = result,
                MessageId = result.NMSMessageId, // Not really used by this framework, but why not
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
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IMessage object.
         */
        return Task.CompletedTask;
    }

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}