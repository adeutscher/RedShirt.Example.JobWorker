using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;
using System.Globalization;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;

internal interface ISequenceNumberStorage
{
    Task<string?> GetLastSequenceNumber(string key, CancellationToken cancellationToken = default);
    Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default);
}

internal class DynamoSequenceNumberStorage(
    IDynamoDBContext dynamoDbContext,
    IAmazonDynamoDB dynamoDb,
    IKinesisRetryWrapperService retryWrapperService,
    IOptions<DynamoSequenceNumberStorage.ConfigurationModel> options,
    IOptions<KinesisConfiguration> kinesisOptions,
    ILogger<DynamoSequenceNumberStorage> logger) : ISequenceNumberStorage
{
    /// <summary>
    ///     Attribute holding the zero-padded sequence number used by the conditional write.
    ///     Not read back: <see cref="Record.Value" /> stays the source of the checkpoint.
    /// </summary>
    internal const string SortableValueAttribute = "SortableValue";

    /// <summary>
    ///     Kinesis allows sequence numbers of up to 129 digits (pattern <c>0|([1-9]\d{0,128})</c>).
    /// </summary>
    internal const int SortableValueWidth = 129;

    private Task<Record?> LoadAsync(string recordKey, CancellationToken cancellationToken)
    {
        return dynamoDbContext.LoadAsync<Record>(recordKey, new LoadConfig
        {
            OverrideTableName = options.Value.TableName
        }, cancellationToken)!;
    }

    /// <summary>
    ///     Read the last sequence number for a shard.
    ///     If there is none under the current key, read the key format used before the stream name was added, so that an
    ///     upgrade carries the checkpoint over instead of replaying the stream. The next save writes the current key.
    /// </summary>
    public Task<string?> GetLastSequenceNumber(string key, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var record = await LoadAsync(KeyHelper.GetCheckpointKey(kinesisOptions.Value.StreamArn, key), ct)
                         ?? await LoadAsync(KeyHelper.GetLegacyCheckpointKey(key), ct);
            return record?.Value;
        }, cancellationToken);
    }

    /// <summary>
    ///     Save the last sequence number for a shard, refusing to move a checkpoint backward.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A checkpoint should only ever be written by the worker holding the shard's lock (see
    ///         <see cref="HighLevelStreamSource" />): the lock is taken before the checkpoint is read and released only after
    ///         the checkpoint is saved. So in normal operation two workers never write the same checkpoint, and the
    ///         condition below never fails.
    ///     </para>
    ///     <para>
    ///         The lock is a Redis lease, not a fence. It is kept alive by background renewal, and the holder is not told
    ///         when renewal fails. The lease can lapse while its holder keeps working if, for example, Redis restarts
    ///         without persistence, Redis fails over to a replica that never received the lock key, a network partition
    ///         between the worker and Redis outlasts the lease, or the process stalls for longer than the lease (CPU
    ///         throttling, a frozen container). Another worker can then take the shard, read the same checkpoint and
    ///         process the same records. Without this condition, whichever worker saved last would win, and a stale worker
    ///         could move the checkpoint backward and cause a replay.
    ///     </para>
    ///     <para>
    ///         The condition only stops the long-term checkpoint moving backward. It does not stop the duplicate
    ///         processing itself (idempotency handling softens that), and the short-term iterator in Redis is an opaque
    ///         string that cannot be compared, so a stale worker can still overwrite it for its few minutes of life.
    ///     </para>
    ///     <para>
    ///         Records written before this condition existed have no <see cref="SortableValueAttribute" />, so the first
    ///         save after an upgrade is unconditional.
    ///     </para>
    /// </remarks>
    public Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [nameof(Record.ShardId)] = new() {S = KeyHelper.GetCheckpointKey(kinesisOptions.Value.StreamArn, key)},
            [nameof(Record.Value)] = new() {S = value},
            [nameof(Record.ExpirationTime)] = new()
            {
                N = (DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                     + 3600 * Math.Max(1, options.Value.RecordDurationHours)
                     - 5).ToString(CultureInfo.InvariantCulture)
            }
        };

        var request = new PutItemRequest
        {
            TableName = options.Value.TableName,
            Item = item
        };

        var sortableValue = ToSortableValue(value);
        if (sortableValue is null)
        {
            // Not a Kinesis sequence number, so it cannot be compared. Save it unconditionally, as before.
            logger.LogWarning(
                "Checkpoint for shard {ShardId} is not a numeric sequence number. Saving it without the ordering check.",
                key);
        }
        else
        {
            item[SortableValueAttribute] = new AttributeValue {S = sortableValue};
            // Equal is allowed, so that saving the same checkpoint again succeeds and refreshes its expiry.
            request.ConditionExpression = "attribute_not_exists(#sortable) OR #sortable <= :sortable";
            request.ExpressionAttributeNames = new Dictionary<string, string> {["#sortable"] = SortableValueAttribute};
            request.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":sortable"] = new() {S = sortableValue}
            };
        }

        return retryWrapperService.RunAsync(async ct =>
        {
            try
            {
                await dynamoDb.PutItemAsync(request, ct);
            }
            catch (ConditionalCheckFailedException)
            {
                /*
                 * A newer checkpoint is already saved, so another worker processed further along this shard while this
                 * worker believed it held the lock. Keep the newer checkpoint. Not an error for this worker's batch: its
                 * records were processed, and the stream position is already past them.
                 */
                logger.LogWarning(
                    "Did not save checkpoint {SequenceNumber} for shard {ShardId}: a later checkpoint is already saved. The shard lock may have lapsed while this worker held it.",
                    value, key);
            }
        }, cancellationToken);
    }

    /// <summary>
    ///     Kinesis sequence numbers are decimal strings of varying length, so they cannot be compared as strings, and they
    ///     are too long for a DynamoDB number (38 digits of precision). Left-padding them with zeros to a fixed width makes
    ///     string order match numeric order.
    ///     Returns null if <paramref name="sequenceNumber" /> is not a decimal sequence number that fits.
    /// </summary>
    internal static string? ToSortableValue(string sequenceNumber)
    {
        if (string.IsNullOrEmpty(sequenceNumber)
            || sequenceNumber.Length > SortableValueWidth
            || !sequenceNumber.All(char.IsAsciiDigit))
        {
            return null;
        }

        return sequenceNumber.PadLeft(SortableValueWidth, '0');
    }

    internal class Record
    {
        [DynamoDBHashKey]
        public string ShardId { get; set; } = string.Empty;

        public string Value { get; init; } = string.Empty;
        public long ExpirationTime { get; set; }
    }

    internal class ConfigurationModel
    {
        public required string TableName { get; init; }
        public required int RecordDurationHours { get; init; }
    }
}