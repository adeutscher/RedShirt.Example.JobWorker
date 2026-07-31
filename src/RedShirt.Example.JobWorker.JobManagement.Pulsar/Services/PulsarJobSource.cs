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

        // success is intentionally unused: stream cursors advance once the batch gate completes,
        // matching the Kafka / Kinesis always-ack / batch-complete-before-commit pattern.
        _ = success;

        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            Session.Increment(pulsarJobModel.MessageId);

            if (!Session.IsComplete)
            {
                return;
            }

            /*
             * Confirming that this double-retry is intentional
             * Most of the important code within the consumer wrapper implementation is itself wrapped by the retryWrapper.
             * Wrapping again just in case there's something exception-worthy coming from another part of the code.
             */
            await retryWrapperService.RunAsync(async ct =>
            {
                var consumer = consumerSource.GetConsumer();
                await consumer.CommitAsync(Session.MessagesToProcess, ct);
            }, cancellationToken);

            Session = null;
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
        var skippedMessages = new List<IPulsarMessageContainer>();
        var totalMessages = new List<IPulsarMessageContainer>();

        foreach (var receivedMessage in messageSourceResponse.Messages)
        {
            totalMessages.Add(receivedMessage);

            var messageBody = receivedMessage.Value;

            if (string.IsNullOrWhiteSpace(messageBody))
            {
                logger.LogWarning("Empty Pulsar message body for {MessageId}; skipping", receivedMessage.MessageId);
                skippedMessages.Add(receivedMessage);
                continue;
            }

            try
            {
                logger.LogTrace("Raw Pulsar message: {MessageBody}", messageBody);

                var @object = converter.Convert(messageBody);
                if (@object is null)
                {
                    logger.LogWarning("Pulsar message conversion returned null for {MessageId}; skipping",
                        receivedMessage.MessageId);
                    // TODO: At the moment, null results during the parsing results in the message being ignored. Putting a pin in this issue until later, as it suggests a need for a dedicated revisit of handling bad messages for streams. Not great, but consistent with Kafka / Kinesis
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
                // TODO: At the moment, exceptions during the parsing results in the message being ignored. Putting a pin in this issue until later, as it suggests a need for a dedicated revisit of handling bad messages for streams. Not great, but consistent with Kafka / Kinesis
                skippedMessages.Add(receivedMessage);
            }
        }

        if (skippedMessages.Count > 0 && skippedMessages.Count == messageSourceResponse.Messages.Count)
        {
            // Skipped every single message (ouch)
            await retryWrapperService.RunAsync(
                ct => consumerSource.GetConsumer().CommitAsync(skippedMessages, ct), cancellationToken);
            return new JobSourceResponse
            {
                Items = []
            };
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
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Subscription heartbeats / flow control are managed by the underlying Pulsar client.
         */
        return Task.CompletedTask;
    }
}
