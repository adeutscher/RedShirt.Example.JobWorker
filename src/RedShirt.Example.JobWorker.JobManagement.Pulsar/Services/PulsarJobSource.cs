using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

internal class PulsarJobSource(
    IPulsarConsumerSource consumerSource,
    IPulsarMessageSource pulsarMessageSource,
    IPulsarRetryWrapperService retryWrapperService,
    ILogger<PulsarJobSource> logger,
    IOptions<PulsarConsumerFactory.ConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not PulsarJobModel pulsarJobModel)
        {
            return;
        }

        /*
         * Confirming that this double-retry is intentional
         * Most of the important code within the consumer wrapper implementation is itself wrapped by the retryWrapper.
         * Wrapping again just in case there's something exception-worthy coming from another part of the code.
         *
         * Shared subscriptions acknowledge per message (SQS-like). Success acks; non-success nacks so Pulsar
         * redelivers and DeadLetterPolicy can move the message to the DLQ after MaxRedeliverCount.
         */
        await retryWrapperService.RunAsync(async ct =>
        {
            var consumer = await consumerSource.GetConsumerAsync(ct);
            if (result.IsSuccessful())
            {
                await consumer.AcknowledgeAsync(pulsarJobModel.Message, ct);
            }
            else
            {
                await consumer.NegativeAcknowledgeAsync(pulsarJobModel.Message, ct);
            }
        }, cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace(
            "Fetching up to {BatchSize} messages from Pulsar Subscription: {SubscriptionName}",
            batchSize, options.Value.SubscriptionName);

        var messageSourceResponse = await pulsarMessageSource.GetMessagesAsync(batchSize, cancellationToken);

        return new JobSourceResponse
        {
            Items = messageSourceResponse.Messages
                .Select(receivedMessage => new PulsarJobModel
                {
                    Message = receivedMessage,
                    CreatedAtUtc = DateTime.UtcNow,
                    Body = receivedMessage.Value
                } as IRawJobModel).ToList()
        };
    }

    /// <summary>
    ///     Unacknowledged messages become eligible for redelivery after AckTimeoutSeconds.
    ///     Subscription delivery / flow control is managed by the Pulsar client.
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Subscription heartbeats / flow control are managed by the underlying Pulsar client.
         * AckTimeout (see JobSource:Pulsar:AckTimeoutSeconds) covers lease expiry for unacked messages.
         */
        return Task.CompletedTask;
    }
}