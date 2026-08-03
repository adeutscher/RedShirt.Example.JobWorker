using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
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
    IOptions<DynamoSequenceNumberStorage.ConfigurationModel> options) : ISequenceNumberStorage
{
    public Task<string?> GetLastSequenceNumber(string key, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(async ct =>
        {
            var record = await dynamoDbContext.LoadAsync<Record>(KeyHelper.GetCheckpointKey(key), new LoadConfig
            {
                OverrideTableName = options.Value.TableName
            }, ct);
            return record?.Value;
        }, cancellationToken);
    }

    public Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(ct =>
            dynamoDbContext.SaveAsync(new Record
            {
                ShardId = KeyHelper.GetCheckpointKey(key),
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