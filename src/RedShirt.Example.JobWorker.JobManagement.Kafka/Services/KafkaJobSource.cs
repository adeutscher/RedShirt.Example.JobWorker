using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal class KafkaJobSource(
    IKafkaConsumerSource consumerSource,
    IKafkaMessageSource kafkaMessageSource,
    ISourceMessageConverter converter,
    ILogger<KafkaJobSource> logger) : IJobSource
{
    internal readonly List<KafkaTrackerSession> Sessions = [];
    private readonly SemaphoreSlim _sessionsSemaphore = new(1, 1);

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not KafkaJobModel kafkaJobModel)
        {
            return;
        }

        // success is intentionally unused: stream offsets advance once the batch gate completes,
        // matching the Kinesis always-ack / batch-complete-before-commit pattern.
        _ = success;

        await _sessionsSemaphore.WaitAsync(cancellationToken);

        try
        {
            var trackerSession = Sessions.FirstOrDefault(s =>
                s.Messages.Any(m => m.MessageId == kafkaJobModel.MessageId));

            if (trackerSession is null)
            {
                return;
            }

            trackerSession.Increment(kafkaJobModel.MessageId);

            if (trackerSession.IsComplete)
            {
                var consumer = consumerSource.GetConsumer();
                consumer.Commit(trackerSession.Messages);
                Sessions.Remove(trackerSession);
            }
        }
        finally
        {
            _sessionsSemaphore.Release();
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await kafkaMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IJobModel>();
        var sessionMessages = new List<IKafkaMessageContainer>();
        var skippedMessages = new List<IKafkaMessageContainer>();

        foreach (var receivedMessage in messages)
        {
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
                    skippedMessages.Add(receivedMessage);
                    continue;
                }

                var data = new KafkaJobModel
                {
                    Message = receivedMessage,
                    CreatedAtUtc = DateTime.UtcNow,
                    Data = @object
                };

                items.Add(data);
                sessionMessages.Add(receivedMessage);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Kafka message: {MessageBody}", messageBody);
                skippedMessages.Add(receivedMessage);
            }
        }

        if (skippedMessages.Count > 0)
        {
            consumerSource.GetConsumer().Commit(skippedMessages);
        }

        if (sessionMessages.Count > 0)
        {
            await _sessionsSemaphore.WaitAsync(cancellationToken);
            try
            {
                Sessions.Add(new KafkaTrackerSession(sessionMessages));
            }
            finally
            {
                _sessionsSemaphore.Release();
            }
        }

        return new JobSourceResponse
        {
            Items = items
        };
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Consumer group heartbeats are managed by librdkafka for the IConsumer lifetime.
         */
        return Task.CompletedTask;
    }
}