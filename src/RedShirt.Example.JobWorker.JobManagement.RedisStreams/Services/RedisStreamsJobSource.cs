using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;

internal class RedisStreamsJobSource(
    IRedisConnectionCacheService redisConnectionCacheService,
    IRedisStreamsRetryWrapperService retryWrapperService,
    ILogger<RedisStreamsJobSource> logger,
    IOptions<RedisStreamsJobSource.ConfigurationModel> options) : IJobSource
{
    private const string UnreadEntriesMarker = ">";

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not RedisStreamRawJobModel redisStreamJobModel)
        {
            return;
        }

        // Intentionally not using result
        // The `_ = result;` phrasing prevents certain code analysis tools from flagging this as a potential issue
        _ = result;

        // Ack whether successful, recoverable failure, or unrecoverable failure.
        // Redis Streams has no built-in DLQ; dead-lettering is handled by IJobFailureHandler
        //  on an application-to-application basis.
        var database = await redisConnectionCacheService.GetDatabaseAsync(cancellationToken);
        await retryWrapperService.RunAsync(ct => database.StreamAcknowledgeAsync(options.Value.StreamName,
            options.Value.GroupName, redisStreamJobModel.Message.Id, CommandFlags.None), cancellationToken);
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
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

        var items = new List<IRawJobModel>();

        foreach (var entry in entries)
        {
            items.Add(new RedisStreamRawJobModel
            {
                Message = entry,
                MessageId = ((string?)entry.Id) ?? "UNKNOWN",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return new JobSourceResponse
        {
            Items = items
        };
    }

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
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
