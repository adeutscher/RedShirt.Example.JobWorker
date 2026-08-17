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

    /// <summary>
    ///     Issues <c>XREADGROUP</c> for new entries (<c>&gt;</c>).
    ///     When <see cref="ConfigurationModel.EffectiveWaitTimeSeconds" /> is 0 this is a non-blocking read
    ///     (current behaviour). When greater than 0, StackExchange.Redis sends <c>BLOCK</c> with <c>COUNT</c>
    ///     equal to <paramref name="batchSize" />.
    ///     <c>BLOCK 0</c> (wait forever) is never used: the wait is capped, and the job-source
    ///     <see cref="CancellationToken" /> is linked via <see cref="Task.WaitAsync(System.Threading.CancellationToken)" />
    ///     so SIGTERM can abandon the wait. The multiplexer is shared with distributed locks/cache;
    ///     a long <c>BLOCK</c> can delay those commands on the same connection — prefer a short block,
    ///     or a dedicated connection for the stream consumer if a short cap is not enough.
    /// </summary>
    private Task<StreamEntry[]> ReadGroupAsync(IDatabase database, int batchSize,
        CancellationToken cancellationToken)
    {
        var waitTimeSeconds = options.Value.EffectiveWaitTimeSeconds;
        if (waitTimeSeconds <= 0)
        {
            // Return short-polling strategy
            return database.StreamReadGroupAsync(
                options.Value.StreamName,
                options.Value.GroupName,
                options.Value.EffectiveConsumerName,
                UnreadEntriesMarker,
                batchSize,
                false,
                CommandFlags.None);
        }

        // Return long-polling strategy
        return database.StreamReadGroupAsync(
            options.Value.StreamName,
            options.Value.GroupName,
            options.Value.EffectiveConsumerName,
            UnreadEntriesMarker,
            batchSize,
            false,
            TimeSpan.FromSeconds(waitTimeSeconds));
    }

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
        await retryWrapperService.RunAsync(async ct =>
        {
            var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
            await database.StreamAcknowledgeAsync(options.Value.StreamName,
                options.Value.GroupName, redisStreamJobModel.Message.Id);
        }, cancellationToken);
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from Redis Stream: {StreamName}",
            batchSize, options.Value.StreamName);

        var entries = await retryWrapperService.RunAsync(async ct =>
        {
            var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
            return await ReadGroupAsync(database, batchSize, ct);
        }, cancellationToken);

        return new JobSourceResponse
        {
            Items = (entries ?? [])
                // ReSharper disable once CanReplaceCastWithLambdaReturnType
                .Select(entry => (IRawJobModel) new RedisStreamRawJobModel
                {
                    Message = entry,
                    MessageId = (string?) entry.Id ?? "UNKNOWN",
                    CreatedAtUtc = DateTime.UtcNow
                }).ToList()
        };
    }

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Upper bound for <c>XREADGROUP BLOCK</c>. Never send <c>BLOCK 0</c> (wait forever):
        ///     shutdown would then depend entirely on cancellation, and the connection would stay
        ///     checked out of the shared multiplexer.
        /// </summary>
        private const int MaximumWaitTimeSeconds = 30;

        public required string StreamName { get; init; }
        public required string GroupName { get; init; }
        public string? ConsumerName { get; init; }

        /// <summary>
        ///     <c>XREADGROUP BLOCK</c> timeout in seconds. 0 (default) is a non-blocking read.
        /// </summary>
        public required int WaitTimeSeconds { get; init; }

        public string EffectiveConsumerName => !string.IsNullOrWhiteSpace(ConsumerName)
            ? ConsumerName
            : Environment.MachineName;

        public int EffectiveWaitTimeSeconds =>
            Math.Min(Math.Max(0, WaitTimeSeconds), MaximumWaitTimeSeconds);
    }
}