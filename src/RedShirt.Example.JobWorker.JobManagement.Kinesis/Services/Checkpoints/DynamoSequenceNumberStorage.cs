using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;

internal interface ISequenceNumberStorage
{
    Task<string?> GetLastSequenceNumber(string key, CancellationToken cancellationToken = default);
    Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default);
}

internal class DynamoSequenceNumberStorage(
    IDynamoDBContext dynamoDbContext,
    IKinesisRetryWrapperService retryWrapperService,
    IOptions<DynamoSequenceNumberStorage.ConfigurationModel> options,
    IOptions<KinesisConfiguration> kinesisOptions) : ISequenceNumberStorage
{
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

    private Task<Record?> LoadAsync(string recordKey, CancellationToken cancellationToken)
    {
        return dynamoDbContext.LoadAsync<Record>(recordKey, new LoadConfig
        {
            OverrideTableName = options.Value.TableName
        }, cancellationToken)!;
    }

    public Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(ct =>
            dynamoDbContext.SaveAsync(new Record
            {
                ShardId = KeyHelper.GetCheckpointKey(kinesisOptions.Value.StreamArn, key),
                Value = value,
                ExpirationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                 + 3600 * Math.Max(1, options.Value.RecordDurationHours)
                                 - 5
            }, new SaveConfig
            {
                OverrideTableName = options.Value.TableName
            }, ct), cancellationToken);
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