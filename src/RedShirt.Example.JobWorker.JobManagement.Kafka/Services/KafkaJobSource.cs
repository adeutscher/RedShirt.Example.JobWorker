using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal class KafkaJobSource(
    IKafkaConsumerSource consumerSource,
    IKafkaMessageSource kafkaMessageSource,
    IKafkaRetryWrapperService retryWrapperService,
    ILogger<KafkaJobSource> logger) : IJobSource
{
    private readonly SemaphoreSlim _sessionSemaphore = new(1, 1);
    internal KafkaTrackerSession? Session;

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not KafkaJobModel kafkaJobModel
            || Session is null)
        {
            return;
        }

        // result is intentionally unused for checkpointing: the topic always advances.
        // Unrecoverable failures are handled via IJobFailureHandler (application DLQ).
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue 
        _ = result;

        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            Session.Increment(kafkaJobModel.MessageId);

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

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (Session is not null)
        {
            /*
             * If a session is already established, then short-circuit and return empty.
             * Like Kinesis, Kafka is more of a stream than a message broker.
             * Unlike Kinesis, fine-grained control of which partition to poll from is considered more of an admin action.
             *
             * The Kafka consumer will instead receive from whichever parition the server(s) assign it.
             * Because the Core library sorts message results and doesn't execute them in the exact order received,
             *  we can't return any more messages at the moment for fear of them being acknowledged out of sequence.
             * Because of this, if a session is already in motion then just return an empty list.
             * An empty list just means more wait time in Loader mode, so if you are using Kafka as a source then it strongly recommended to use Batch mode for polling.
             */
            return new JobSourceResponse
            {
                Items = []
            };
        }

        var messageSourceResponse = await kafkaMessageSource.GetMessagesAsync(batchSize, cancellationToken);

        var items = new List<IRawJobModel>();
        var messagesToProcess = new List<IKafkaMessageContainer>();
        var skippedMessages = new List<IKafkaMessageContainer>();
        var totalMessages = new List<IKafkaMessageContainer>();

        foreach (var receivedMessage in messageSourceResponse.Messages)
        {
            totalMessages.Add(receivedMessage);

            var messageBody = receivedMessage.Value;

            if (string.IsNullOrWhiteSpace(messageBody))
            {
                logger.LogWarning("Empty Kafka message body for {MessageId}; skipping", receivedMessage.MessageId);
                skippedMessages.Add(receivedMessage);
                continue;
            }

            var data = new KafkaJobModel
            {
                Message = receivedMessage,
                CreatedAtUtc = DateTime.UtcNow,
                Body = messageBody
            };

            items.Add(data);
            messagesToProcess.Add(receivedMessage);
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
                Session = new KafkaTrackerSession(totalMessages, messagesToProcess);
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
    ///     Being from a stream-based message source, Kafka messages do not need heartbeats.
    ///     The ownership of the client over the underlying topic partition for the consumer group is managed by the Kafka
    ///     protocol.
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Consumer group heartbeats are managed by underlying Kafka protocol for the IConsumer lifetime.
         */
        return Task.CompletedTask;
    }
}