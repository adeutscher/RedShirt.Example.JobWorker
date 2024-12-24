using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal interface ISequenceNumberStorage
{
    Task<string?> GetLastSequenceNumber(string key, CancellationToken cancellationToken = default);
    Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default);
}

internal class DynamoSequenceNumberStorage(
    IDynamoDBContext dynamoDbContext,
    IOptions<DynamoSequenceNumberStorage.ConfigurationModel> options) : ISequenceNumberStorage
{
    public async Task<string?> GetLastSequenceNumber(string key, CancellationToken cancellationToken = default)
    {
        var record = await dynamoDbContext.LoadAsync<Record>(KeyHelper.GetCheckpointKey(key),
            new DynamoDBOperationConfig
            {
                OverrideTableName = options.Value.TableName
            }, cancellationToken);
        return record?.Value;
    }

    public Task SetLastSequenceNumber(string key, string value, CancellationToken cancellationToken = default)
    {
        return dynamoDbContext.SaveAsync(new Record
        {
            ShardId = KeyHelper.GetCheckpointKey(key),
            Value = value,
            ExpirationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                             + 3600 * Math.Max(1, options.Value.RecordDurationHours)
                             - 5
        }, new DynamoDBOperationConfig
        {
            OverrideTableName = options.Value.TableName
        }, cancellationToken);
    }

    internal class Record
    {
        [DynamoDBHashKey]
        public string ShardId { get; set; } = string.Empty;

        public string Value { get; init; } = string.Empty;
        public long ExpirationTime { get; set; }
    }

    public class ConfigurationModel
    {
        public required string TableName { get; init; }
        public required int RecordDurationHours { get; init; }
    }
}