using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface ILowLevelStreamSource
{
    /// <summary>
    ///     Get Kinesis records from a specific shard.
    /// </summary>
    /// <param name="batchSize"></param>
    /// <param name="shardName"></param>
    /// <param name="iteratorString"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<StreamSourceResponse> GetJobsAsync(int batchSize, string shardName, string iteratorString,
        CancellationToken cancellationToken = default);
}

internal class LowLevelStreamSource(
    IAmazonKinesis kinesisClient,
    ILogger<LowLevelStreamSource> logger,
    IOptions<KinesisConfiguration> options) : ILowLevelStreamSource
{
    public async Task<StreamSourceResponse> GetJobsAsync(int batchSize, string shardName, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        GetRecordsResponse kinesisResponse;

        try
        {
            kinesisResponse = await kinesisClient.GetRecordsAsync(new GetRecordsRequest
            {
                Limit = batchSize,
                StreamARN = options.Value.StreamArn,
                ShardIterator = iteratorString
            }, cancellationToken);
        }
        catch (ExpiredIteratorException)
        {
            // Not an event worth worrying about
            // Just skip over this shard for the moment and loop around to it in another invocation
            return new StreamSourceResponse
            {
                IteratorString = string.Empty,
                Items = [],
                LastSequenceNumber = null
            };
        }

        var items = new List<IRawJobModel>();

        string? lastSequenceNumber = null;

        foreach (var item in kinesisResponse.Records)
        {
            var body = Encoding.UTF8.GetString(item.Data.ToArray());
            // Update last sequence number.
            // Can only do this because the result of GetRecordsAsync is in the correct order raw from the shard. 
            lastSequenceNumber = item.SequenceNumber;

            logger.LogTrace("Raw Kinesis message: {MessageBody}", body);

            var data = new KinesisJobModel
            {
                ShardId = shardName,
                MessageId = item.SequenceNumber,
                CreatedAtUtc = DateTime.UtcNow,
                Body = body
            };

            items.Add(data);
        }

        return new StreamSourceResponse
        {
            IteratorString = kinesisResponse.NextShardIterator,
            Items = items,
            LastSequenceNumber = lastSequenceNumber
        };
    }
}