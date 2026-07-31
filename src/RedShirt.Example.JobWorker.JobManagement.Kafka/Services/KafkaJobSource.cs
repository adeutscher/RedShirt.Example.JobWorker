using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal class KafkaJobSource(
    IKafkaConsumerSource consumerSource,
    IKafkaMessageSource kafkaMessageSource,
    IKafkaRetryWrapperService retryWrapperService,
    ISourceMessageConverter converter,
    ILogger<KafkaJobSource> logger) : IJobSource
{
    private readonly SemaphoreSlim _sessionSemaphore = new(1, 1);
    internal KafkaTrackerSession? Session;

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not KafkaJobModel kafkaJobModel
            || Session is null)
        {
            return;
        }

        // success is intentionally unused: stream offsets advance once the batch gate completes,
        // matching the Kinesis always-ack / batch-complete-before-commit pattern.
        _ = success;

        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            Session.Increment(kafkaJobModel.MessageId);

            if (!Session.IsComplete)
            {
                return;
            }

            var consumer = consumerSource.GetConsumer();
            await retryWrapperService.RunAsync(
                _ =>
                {
                    consumer.Commit(Session.MessagesToProcess);
                    return Task.CompletedTask;
                },
                cancellationToken);
            Session = null;
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messageSourceResponse = await kafkaMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IJobModel>();
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

            try
            {
                logger.LogTrace("Raw Kafka message: {MessageBody}", messageBody);

                var @object = converter.Convert(messageBody);
                if (@object is null)
                {
                    logger.LogWarning("Kafka message conversion returned null for {MessageId}; skipping",
                        receivedMessage.MessageId);
                    // TODO: At the moment, null results during the parsing results in the message being ignored. Putting a pin in this issue until later, as it suggests a need for a dedicated revisit of handling bad messages for streams. Not great, but consistent with Kinesis
                    continue;
                }

                var data = new KafkaJobModel
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
                logger.LogWarning(e, "Error parsing Kafka message: {MessageBody}", messageBody);
                // TODO: At the moment, exceptions during the parsing results in the message being ignored. Putting a pin in this issue until later, as it suggests a need for a dedicated revisit of handling bad messages for streams. Not great, but consistent with Kinesis
                skippedMessages.Add(receivedMessage);
            }
        }

        if (skippedMessages.Count > 0 && skippedMessages.Count == messageSourceResponse.Messages.Count)
        {
            // Skipped every single message (ouch)
            await retryWrapperService.RunAsync(
                _ =>
                {
                    var consumer = consumerSource.GetConsumer();
                    consumer.Commit(skippedMessages);
                    return Task.CompletedTask;
                },
                cancellationToken);
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

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Consumer group heartbeats are managed by underlying Kafka protocol for the IConsumer lifetime.
         */
        return Task.CompletedTask;
    }
}