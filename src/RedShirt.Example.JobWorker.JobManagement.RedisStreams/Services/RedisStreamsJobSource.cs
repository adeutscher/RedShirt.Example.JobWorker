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
    ///     Takes over entries that were delivered to a consumer but not acknowledged for longer than the reclaim idle
    ///     time, such as entries held by a worker that crashed or was stopped mid-batch.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reading with ">" only ever returns new entries, so without this an entry delivered to a worker that died
    ///         stays pending forever. That is true even when the replacement worker uses the same consumer name.
    ///     </para>
    ///     <para>
    ///         Entries already delivered <see cref="ConfigurationModel.EffectiveMaximumDeliveries" /> times are
    ///         acknowledged without running, so that an entry which crashes the worker every time cannot loop forever.
    ///         Redis Streams has no dead-letter queue, so such entries are only logged.
    ///     </para>
    ///     <para>
    ///         The scan always starts from the oldest pending entry. Every entry that it returns is either claimed,
    ///         acknowledged, or was claimed by another worker in the meantime, which resets its idle time. So the next
    ///         scan moves on to other entries, and no cursor needs to be kept.
    ///     </para>
    ///     <para>
    ///         XCLAIM is sent with the same minimum idle time, so when two workers scan at once, only one of them
    ///         claims each entry.
    ///     </para>
    ///     <para>
    ///         Needs Redis 6.2 or later, for the IDLE option of XPENDING.
    ///     </para>
    /// </remarks>
    private async Task<StreamEntry[]> ReclaimIdleEntriesAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!options.Value.IsPendingReclaimEnabled || batchSize <= 0)
        {
            return [];
        }

        var minimumIdleMilliseconds = options.Value.EffectivePendingReclaimIdleSeconds * 1000L;

        var pending = await retryWrapperService.RunAsync(async ct =>
        {
            var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
            return await database.StreamPendingMessagesAsync(
                options.Value.StreamName,
                options.Value.GroupName,
                batchSize,
                RedisValue.Null,
                null,
                null,
                minimumIdleMilliseconds);
        }, cancellationToken);

        if (pending.Length == 0)
        {
            return [];
        }

        var maximumDeliveries = options.Value.EffectiveMaximumDeliveries;
        var toDrop = new List<RedisValue>();
        var toClaim = new List<RedisValue>();
        foreach (var info in pending)
        {
            if (info.DeliveryCount >= maximumDeliveries)
            {
                logger.LogWarning(
                    "Dropping Redis stream entry {MessageId} without running it: it was delivered {DeliveryCount} times (limit {MaximumDeliveries}) without being acknowledged. It was last held by consumer {ConsumerName}.",
                    (string?) info.MessageId, info.DeliveryCount, maximumDeliveries, (string?) info.ConsumerName);
                toDrop.Add(info.MessageId);
            }
            else
            {
                toClaim.Add(info.MessageId);
            }
        }

        if (toDrop.Count > 0)
        {
            await retryWrapperService.RunAsync(async ct =>
            {
                var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
                await database.StreamAcknowledgeAsync(options.Value.StreamName, options.Value.GroupName,
                    toDrop.ToArray());
            }, cancellationToken);
        }

        if (toClaim.Count == 0)
        {
            return [];
        }

        var claimed = await retryWrapperService.RunAsync(async ct =>
        {
            var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
            return await database.StreamClaimAsync(
                options.Value.StreamName,
                options.Value.GroupName,
                options.Value.EffectiveConsumerName,
                minimumIdleMilliseconds,
                toClaim.ToArray());
        }, cancellationToken);

        // An entry trimmed from the stream while pending has no content to run. Redis 7 removes those from the pending
        // list during the claim. Older versions keep them, and they are dropped once they reach the delivery limit.
        var reclaimed = claimed.Where(entry => !entry.IsNull).ToArray();
        if (reclaimed.Length > 0)
        {
            logger.LogInformation(
                "Reclaimed {Count} Redis stream entries that were idle for at least {IdleSeconds} seconds.",
                reclaimed.Length, options.Value.EffectivePendingReclaimIdleSeconds);
        }

        return reclaimed;
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

    /// <summary>
    ///     Heartbeats keep in-flight entries from looking idle, so that other workers do not reclaim them.
    ///     Half the reclaim idle time leaves room for a late heartbeat. Disabled when reclaiming is disabled.
    /// </summary>
    public int RecommendedHeartbeatIntervalSeconds => options.Value.IsPendingReclaimEnabled
        ? Math.Max(1, (int) Math.Ceiling(options.Value.EffectivePendingReclaimIdleSeconds * 0.5))
        : 0;

    public bool IsSubscriptionSource => false;

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from Redis Stream: {StreamName}",
            batchSize, options.Value.StreamName);

        var reclaimed = await ReclaimIdleEntriesAsync(batchSize, cancellationToken);

        var remaining = batchSize - reclaimed.Length;
        var entries = remaining <= 0
            ? []
            : await retryWrapperService.RunAsync(async ct =>
            {
                var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
                return await database.StreamReadGroupAsync(
                    options.Value.StreamName,
                    options.Value.GroupName,
                    options.Value.EffectiveConsumerName,
                    UnreadEntriesMarker,
                    remaining,
                    false,
                    CommandFlags.None);
            }, cancellationToken);

        return new JobSourceResponse
        {
            Items = reclaimed.Concat(entries)
                // ReSharper disable once CanReplaceCastWithLambdaReturnType
                .Select(entry => (IRawJobModel) new RedisStreamRawJobModel
                {
                    Message = entry,
                    MessageId = (string?) entry.Id ?? "UNKNOWN",
                    CreatedAtUtc = DateTime.UtcNow
                }).ToList()
        };
    }

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (!options.Value.IsPendingReclaimEnabled || message is not RedisStreamRawJobModel redisStreamJobModel)
        {
            return;
        }

        /*
         * Claiming an entry for this consumer with a minimum idle time of 0 resets its idle time. JUSTID (the ids-only
         * form) leaves the delivery count alone, so heartbeats do not count toward MaximumDeliveries.
         *
         * XCLAIM does not check the current owner. If this worker stalled for longer than the reclaim idle time and
         * another worker reclaimed the entry, this heartbeat takes it back. Both workers are already running the job
         * by then, so this does not add a duplicate, and either worker's ack clears the entry for the group.
         */
        await retryWrapperService.RunAsync(async ct =>
        {
            var database = await redisConnectionCacheService.GetDatabaseAsync(ct);
            await database.StreamClaimIdsOnlyAsync(
                options.Value.StreamName,
                options.Value.GroupName,
                options.Value.EffectiveConsumerName,
                0,
                [redisStreamJobModel.Message.Id]);
        }, cancellationToken);
    }

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public sealed class ConfigurationModel
    {
        public const int DefaultPendingReclaimIdleSeconds = 300;
        public const int DefaultMaximumDeliveries = 5;

        public required string StreamName { get; init; }
        public required string GroupName { get; init; }
        public string? ConsumerName { get; init; }

        public string EffectiveConsumerName => !string.IsNullOrWhiteSpace(ConsumerName)
            ? ConsumerName
            : Environment.MachineName;

        /// <summary>
        ///     How long an entry must go unacknowledged, with no heartbeat, before another worker may reclaim it.
        ///     Unset means <see cref="DefaultPendingReclaimIdleSeconds" />. Zero or negative disables reclaiming and
        ///     heartbeats.
        /// </summary>
        public int? PendingReclaimIdleSeconds { get; init; }

        public int EffectivePendingReclaimIdleSeconds =>
            PendingReclaimIdleSeconds ?? DefaultPendingReclaimIdleSeconds;

        public bool IsPendingReclaimEnabled => EffectivePendingReclaimIdleSeconds > 0;

        /// <summary>
        ///     How many times an entry may be delivered before a reclaim drops it instead of running it again.
        ///     Unset or not positive means <see cref="DefaultMaximumDeliveries" />.
        /// </summary>
        public int? MaximumDeliveries { get; init; }

        public int EffectiveMaximumDeliveries =>
            MaximumDeliveries is > 0 ? MaximumDeliveries.Value : DefaultMaximumDeliveries;
    }
}