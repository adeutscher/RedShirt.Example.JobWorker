using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Utility;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;

internal class RedisStreamsJobSource(
    IRedisConnectionCacheService redisConnectionCacheService,
    IRedisStreamsRetryWrapperService retryWrapperService,
    IRedisStreamBodyRetriever bodyRetriever,
    ISourceMessageConverter converter,
    ILogger<RedisStreamsJobSource> logger,
    IOptions<RedisStreamsJobSource.ConfigurationModel> options) : IJobSource
{
    private const string UnreadEntriesMarker = ">";

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not RedisStreamJobModel redisStreamJobModel)
        {
            return;
        }

        _ = success;

        var database = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);
        await retryWrapperService.RunAsync(ct => database.StreamAcknowledgeAsync(options.Value.StreamName,
            options.Value.GroupName, redisStreamJobModel.Message.Id, CommandFlags.None), cancellationToken);
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from Redis Stream: {StreamName}",
            batchSize, options.Value.StreamName);

        var database = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);
        var entries = await retryWrapperService.RunAsync(ct => database.StreamReadGroupAsync(
            options.Value.StreamName,
            options.Value.GroupName,
            options.Value.EffectiveConsumerName,
            UnreadEntriesMarker,
            batchSize,
            false,
            CommandFlags.None), cancellationToken);

        var items = new List<IJobModel>();

        foreach (var entry in entries)
        {
            IJobDataModel? convertedMessage = null;
            string? body = null;
            try
            {
                body = bodyRetriever.GetMessageBody(entry.Values);
                convertedMessage = converter.Convert(body);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Redis stream message: {MessageBody}", body);
            }

            if (convertedMessage is null)
            {
                await retryWrapperService.RunAsync(ct => database.StreamAcknowledgeAsync(options.Value.StreamName,
                    options.Value.GroupName, entry.Id, CommandFlags.None), cancellationToken);
                continue;
            }

            items.Add(new RedisStreamJobModel
            {
                Message = entry,
                MessageId = ((string?)entry.Id) ?? "UNKNOWN",
                IdempotencyId = bodyRetriever.GetIdempotencyId(entry.Values),
                CreatedAtUtc = DateTime.UtcNow,
                Data = convertedMessage
            });
        }

        return new JobSourceResponse
        {
            Items = items
        };
    }

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        public required string StreamName { get; init; }
        public required string GroupName { get; init; }
        public string? ConsumerName { get; init; }

        public string EffectiveConsumerName => !string.IsNullOrWhiteSpace(ConsumerName)
            ? ConsumerName
            : Environment.MachineName;
    }
}
