using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

internal class PulsarJobSource(
    IPulsarConsumerSource consumerSource,
    IPulsarMessageSource pulsarMessageSource,
    IPulsarRetryWrapperService retryWrapperService,
    ISourceMessageConverter converter,
    ILogger<PulsarJobSource> logger) : IJobSource
{
    private readonly SemaphoreSlim _sessionSemaphore = new(1, 1);
    internal PulsarTrackerSession? Session;

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not PulsarJobModel pulsarJobModel
            || Session is null)
        {
            return;
        }

        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (!Session.Contains(pulsarJobModel.MessageId))
            {
                return;
            }

            /*
             * Confirming that this double-retry is intentional
             * Most of the important code within the consumer wrapper implementation is itself wrapped by the retryWrapper.
             * Wrapping again just in case there's something exception-worthy coming from another part of the code.
             *
             * Success acknowledges the message. Failure negatively acknowledges so Pulsar redelivers and the
             * consumer DeadLetterPolicy can move the message to the DLQ after MaxRedeliverCount.
             */
            await retryWrapperService.RunAsync(async ct =>
            {
                var consumer = consumerSource.GetConsumer();
                if (success)
                {
                    await consumer.AcknowledgeAsync(pulsarJobModel.Message, ct);
                }
                else
                {
                    await consumer.NegativeAcknowledgeAsync(pulsarJobModel.Message, ct);
                }
            }, cancellationToken);

            Session.Increment(pulsarJobModel.MessageId);

            if (Session.IsComplete)
            {
                Session = null;
            }
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (Session is not null)
        {
            /*
             * If a session is already established, then short-circuit and return empty.
             * Like Kafka / Kinesis, Pulsar is more of a stream than a classic queue broker.
             *
             * Because the Core library sorts message results and doesn't execute them in the exact order received,
             *  we can't return any more messages at the moment for fear of them being acknowledged out of sequence.
             * Because of this, if a session is already in motion then just return an empty list.
             * An empty list just means more wait time in Loader mode, so if you are using Pulsar as a source then it
             *  is strongly recommended to use Batch mode for polling.
             */
            return new JobSourceResponse
            {
                Items = []
            };
        }

        var messageSourceResponse = await pulsarMessageSource.GetMessagesAsync(batchSize, cancellationToken);

        var items = new List<IJobModel>();
        var messagesToProcess = new List<IPulsarMessageContainer>();
        var deadLetterMessages = new List<IPulsarMessageContainer>();
        var totalMessages = new List<IPulsarMessageContainer>();

        foreach (var receivedMessage in messageSourceResponse.Messages)
        {
            totalMessages.Add(receivedMessage);

            var messageBody = receivedMessage.Value;

            if (string.IsNullOrWhiteSpace(messageBody))
            {
                logger.LogWarning("Empty Pulsar message body for {MessageId}; negatively acknowledging",
                    receivedMessage.MessageId);
                deadLetterMessages.Add(receivedMessage);
                continue;
            }

            try
            {
                logger.LogTrace("Raw Pulsar message: {MessageBody}", messageBody);

                var @object = converter.Convert(messageBody);
                if (@object is null)
                {
                    logger.LogWarning(
                        "Pulsar message conversion returned null for {MessageId}; negatively acknowledging",
                        receivedMessage.MessageId);
                    deadLetterMessages.Add(receivedMessage);
                    continue;
                }

                var data = new PulsarJobModel
                {
                    Message = receivedMessage,
                    CreatedAtUtc = DateTime.UtcNow,
                    Data = @object
                };

                items.Add(data);
                messagesToProcess.Add(receivedMessage);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Pulsar message: {MessageBody}", messageBody);
                deadLetterMessages.Add(receivedMessage);
            }
        }

        if (deadLetterMessages.Count > 0)
        {
            /*
             * Unparseable / empty messages are negatively acknowledged immediately so they redeliver into
             * DeadLetterPolicy (same role as Azure Service Bus DeadLetterMessageAsync for poison payloads).
             */
            await retryWrapperService.RunAsync(
                ct => consumerSource.GetConsumer().NegativeAcknowledgeAsync(deadLetterMessages, ct),
                cancellationToken);
        }

        // ReSharper disable once InvertIf
        if (messagesToProcess.Count > 0)
        {
            await _sessionSemaphore.WaitAsync(cancellationToken);
            try
            {
                Session = new PulsarTrackerSession(totalMessages, messagesToProcess);
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        return new JobSourceResponse
        {
            Items = items
        };
    }

    /// <summary>
    ///     Being from a stream-based message source, Pulsar messages do not need heartbeats.
    ///     Subscription cursor ownership / delivery is managed by the Pulsar protocol.
    ///     Unacknowledged messages become eligible for redelivery after AckTimeoutSeconds.
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Subscription heartbeats / flow control are managed by the underlying Pulsar client.
         * AckTimeout (see JobSource:Pulsar:AckTimeoutSeconds) covers lease expiry for unacked messages.
         */
        return Task.CompletedTask;
    }
}